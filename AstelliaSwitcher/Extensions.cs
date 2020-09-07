using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace AstelliaSwitcher
{
    public static class Extensions
    {
        public unsafe static void ReplaceWith(this MethodBase self, MethodBase newMethod)
        {
            int* p_original = (int*)self.MethodHandle.Value.ToPointer() + 2;
            int* p_patch = (int*)newMethod.MethodHandle.Value.ToPointer() + 2;

            *p_original = *p_patch;
        }
    }
}
