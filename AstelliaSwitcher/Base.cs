using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Permissions;
using System.Text;
using Newtonsoft.Json;

namespace AstelliaSwitcher
{
	// Please don't blame me for putting everything in one file <3
	// I don't know why there are so many comments
	public class Server
	{
		public string Name;
		public string Url;
		public Server(string name, string url)
		{
			Name = name;
			Url = url;
		}
	}

	public class Base
	{
		public static Dictionary<string, string> ConfigLines = new Dictionary<string, string>();

		public static FieldInfo urlField;

		public static string currentServer;

		private static readonly string configPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config");

		[DllImport("user32.dll")]
		static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[DllImport("kernel32.dll")]
		private static extern IntPtr GetConsoleWindow();

		public static string GetValue(string key)
		{
			foreach (var lines in File.ReadAllLines(configPath))
			{
				var split = lines.Split('=');
				if (split[0] == key)
					return split[1];
			}
			return "";
		}

		public static void SetValue(string key, string value)
		{
			if (File.Exists(configPath))
			{
				ConfigLines[key] = value;
				SaveConfig();
				LoadConfig();
			}
		}

		public static void LoadConfig()
		{
			if (File.Exists(configPath))
			{
				ConfigLines["OsuPath"] = GetValue("OsuPath");
				ConfigLines["Servers"] = GetValue("Servers");
				ConfigLines["MD5"] = GetValue("MD5"); // last md5
				ConfigLines["urlField"] = GetValue("urlField");
				ConfigLines["setUrlMethod"] = GetValue("setUrlMethod");
				ConfigLines["checkCertificateMethod"] = GetValue("checkCertificateMethod");
				ConfigLines["FileNameMethod"] = GetValue("FileNameMethod");
				ConfigLines["FullPathMethod"] = GetValue("FullPathMethod");
				ConfigLines["IsTrustedMethod"] = GetValue("IsTrustedMethod");
			}
		}

		public static void SaveConfig()
		{
			if (File.Exists(configPath))
			{
				var builder = new StringBuilder();

				foreach (var line in ConfigLines)
				{
					builder.AppendLine($"{line.Key}={line.Value}");
				}
				File.WriteAllText(configPath, builder.ToString());
			}
		}

		public static string ComputeFile(string filename)
		{
			using (var md5 = MD5.Create())
			{
				using (var stream = File.OpenRead(filename))
				{
					var hash = md5.ComputeHash(stream);
					return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
				}
			}
		}

		public static bool TryCreateConfig()
		{
			if (File.Exists(configPath)) return false;

			var builder = new StringBuilder();

			foreach (var line in ConfigLines)
			{
				builder.AppendLine($"{line.Key}={line.Value}");
			}
			File.WriteAllText(configPath, builder.ToString());

			return true;
		}

		[STAThread]
		static void Main(string[] args)
		{
			if (!File.Exists(configPath))
			{
				if (args.Length < 1)
				{
					Console.WriteLine("Please pass us osu!.exe file");
					Console.ReadKey();
					return;
				}

				else if (args.Length > 0 && args[0].Contains("osu!.exe"))
				{
					TryCreateConfig();
					SetValue("OsuPath", args[0]);
				}
				else
				{
					Console.WriteLine("That's not osu!.exe");
					Console.ReadKey();
					return;
				}
			}

			LoadConfig();

			var osuPath = GetValue("OsuPath");

			var servers = JsonConvert.DeserializeObject<List<Server>>(GetValue("Servers"));

			// Initialize servers, if they're don't exist
			if (servers == null)
			{
				var serverList = new List<Server>();

				serverList.Add(new Server("Astellia", "astellia.club"));
				serverList.Add(new Server("Kurikku", "kurikku.pw"));
				serverList.Add(new Server("Gatari", "osu.gatari.pw"));
				serverList.Add(new Server("Ainu", "ainu.pw"));
				serverList.Add(new Server("Ripple", "ripple.moe"));

				SetValue("Servers", JsonConvert.SerializeObject(serverList));

				servers = JsonConvert.DeserializeObject<List<Server>>(GetValue("Servers"));
			}
			// End of initialization

			var assembly = Assembly.LoadFrom(osuPath);

			var module = assembly.Modules.First();

			// > Mapping
			// - Used for finding osu!'s methods and fields
			// - Caches methods and fields as metadata token (because it allows us to easily access them)
			var currentMd5 = ComputeFile(osuPath);
			if (ConfigLines["MD5"] != currentMd5)
			{
				SetValue("MD5", currentMd5);

				Type webRequest = null;

				foreach (var type in assembly.GetTypes())
				{
					foreach (var field in type.GetFields())
					{
						if (field.FieldType == typeof(Dictionary<string, byte[]>)) // This will find for pFileWebRequest
						{                                                          // and then get base type which is pWebRequest
							webRequest = type.BaseType;                            // (it was actually supposed to found pWebRequest directly )
							break;
						}
					}
				}

				var fakeEntryPointBytes = assembly.EntryPoint.GetMethodBody().GetILAsByteArray(); // eazfuscator's obfuscated entry point

				var entryPoint = module.ResolveMethod(BitConverter.ToInt32(fakeEntryPointBytes, 1)); // aka real osu! entry point, 
																									 // we skip 'call' opcode and parsing metadata token
				var osuMain = entryPoint.DeclaringType;

				// Wanted to use but then found problem and commented this
				/* var isTrustedMetadataToken = BitConverter.ToInt32(entryPoint.GetMethodBody().GetILAsByteArray(), 31);
				SetValue("IsTrustedMethod", Convert.ToString(isTrustedMetadataToken)); */

				MethodInfo fileName = null;
				MethodInfo fullPath = null;

				foreach (var mainMethod in osuMain.GetRuntimeMethods())
				{
					var ilBody = mainMethod.GetMethodBody()?.GetILAsByteArray();
					if (!(ilBody is null))
					{
						if (ilBody.Length == 11)
						{
							if (ilBody[1] == 71 && ilBody[6] == 2)
							{
								fileName = mainMethod;
							}
						}
						else if (ilBody.Length == 21)
						{
							if (ilBody[0] == 126 && ilBody[5] == 37)
							{
								fullPath = mainMethod;
							}
						}
						if (!(fileName is null) && !(fullPath is null))
							break;

					}
				}


				SetValue("FileNameMethod", Convert.ToString(fileName.MetadataToken));
				SetValue("FullPathMethod", Convert.ToString(fullPath.MetadataToken));

				MethodInfo setUrlMethodInit = null;
				MethodInfo checkCertificateMethodInit = null;

				int urlFieldMetadataToken = 0;
				foreach (var method in webRequest.GetRuntimeMethods())
				{
					var body = method.GetMethodBody()?.GetILAsByteArray();

					if (!(body is null))
					{
						if (body.Length == 64)
						{
							if (body[0] == 3 && body[58] == 125)
							{
								setUrlMethodInit = method;
								urlFieldMetadataToken = BitConverter.ToInt32(body, 59);
							}
						}
						else if (body.Length == 33)
						{
							if (body[0] == 23 && body[5] == 1)
								checkCertificateMethodInit = method;
						}
						else if (!(setUrlMethodInit is null) && !(checkCertificateMethodInit is null) && urlFieldMetadataToken != 0)
							break;
					}
				}


				SetValue("setUrlMethod", Convert.ToString(setUrlMethodInit.MetadataToken));
				SetValue("checkCertificateMethod", Convert.ToString(checkCertificateMethodInit.MetadataToken));
				SetValue("urlField", Convert.ToString(urlFieldMetadataToken));

			}
			// End of mapping scope

			// Methods and field initialization
			var checkCertificateMethod = module.ResolveMethod(int.Parse(ConfigLines["checkCertificateMethod"]));
			var setUrlMethod = module.ResolveMethod(int.Parse(ConfigLines["setUrlMethod"]));
			var fileNameMethod = module.ResolveMethod(int.Parse(ConfigLines["FileNameMethod"]));
			var fullPathMethod = module.ResolveMethod(int.Parse(ConfigLines["FullPathMethod"]));
			//var isTrustedMethod = module.ResolveMethod(int.Parse(ConfigLines["IsTrustedMethod"]));

			urlField = module.ResolveField(int.Parse(ConfigLines["urlField"]));
			// End of initialization

			// Patching methods
			setUrlMethod.ReplaceWith(typeof(Base).GetMethod("set_Url"));
			checkCertificateMethod.ReplaceWith(typeof(Base).GetMethod("checkCertificate"));
			fileNameMethod.ReplaceWith(typeof(Base).GetMethod("Filename"));
			fullPathMethod.ReplaceWith(typeof(Base).GetMethod("FullPath"));
			// End of patching

			// Server selector
			choose:
			Console.Clear();

			Console.WriteLine("1. Add server");
			Console.WriteLine("2. Select server");

			switch (Console.ReadKey(false).Key)
			{
				case ConsoleKey.D1:
					Console.Clear();
					Console.Write("Server name (e.g: Astellia): ");
					var serverName = Console.ReadLine();

					Console.Write("Server URL without protocol (e.g: astellia.club): ");
					var serverUrl = Console.ReadLine();

					servers.Add(new Server(serverName, serverUrl));

					SetValue("Servers", JsonConvert.SerializeObject(servers));
					goto choose;
				case ConsoleKey.D2:
					Console.Clear();
					Console.WriteLine("Servers: ");

					for (var i = 0; i < servers.Count; i++)
					{
						Console.WriteLine($"[{i + 1}] {servers[i].Name}");
					}

					Console.Write("Server index: ");
					var index = Convert.ToInt32(Console.ReadLine());

					Console.Clear();

					currentServer = servers[index - 1].Url;
				break;

			}
			// End of server selector

			ShowWindow(GetConsoleWindow(), 0);

			assembly.EntryPoint.Invoke(null, null);
		}

		// Patched methods
		public static string FullPath() => ConfigLines["OsuPath"];
		public static string Filename() => Path.GetFileName(ConfigLines["OsuPath"]);
		public void checkCertificate() { }

		public void set_Url(string value)
		{
			if (value.StartsWith("https://osu."))
				value = value.Replace("osu.ppy.sh", currentServer);
			if (value.StartsWith("https://c") && value.EndsWith("ppy.sh"))
			{
				var serverIndex = value.ToCharArray()[9]; // gets number after c (e.g c) 4 <- (.ppy.sh)
				value = value.Replace(serverIndex + ".ppy.sh", $".{currentServer}");
			}
			if (value.Contains("ppy.sh"))
				value = value.Replace("ppy.sh", currentServer);

			urlField.SetValue(this, value);
		}

		// public bool IsTrusted() => true; 
	}	// End of patched methods
}
