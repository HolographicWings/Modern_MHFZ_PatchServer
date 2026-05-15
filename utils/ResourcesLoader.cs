using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Modern_MHFZ_PatchServer.utils
{
    internal class ResourcesLoader
    {
        // Load embed resource
        public static byte[] LoadResource(string resourceName)
        {
            Assembly asm = Assembly.GetExecutingAssembly();

            using Stream? stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new InvalidOperationException($"Resource not found: '{resourceName}'.");

            byte[] data = new byte[stream.Length];
            stream.ReadExactly(data);
            return data;
        }
    }
}
