using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Modern_MHFZ_PatchServer.utils
{
    internal static class Checksum
    {
        // Generates the SHA256 checksum of a file.
        public static string ComputeSha256(string filePath)
        {
            if(!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);

            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
