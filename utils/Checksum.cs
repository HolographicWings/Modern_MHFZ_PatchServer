using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
/*using System.IO;
using System.IO.Hashing;*/

namespace Modern_MHFZ_PatchServer.utils
{
    internal static class Checksum
    {
        // Generates the SHA256 checksum of a file.
        public static string ComputeSha256(string filePath)
        {
            if(!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);

            using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 1024, options: FileOptions.SequentialScan);

            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        // Generates the CRC32 checksum of a file.
        /*public static string ComputeCrc32(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);

            using FileStream stream = new(filePath,  FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 1024, options: FileOptions.SequentialScan);

            var crc32 = new Crc32();
            crc32.Append(stream);

            return crc32.GetCurrentHashAsUInt32().ToString("x8");
        }*/
    }
}
