using System;
using System.Collections.Generic;
using System.Text;

namespace Modern_MHFZ_PatchServer.utils
{
    // Determines if a file is relevant for patching based on its name, extension, and directory path.
    internal static class FileRevelance
    {
        public static readonly string[] fileBlacklist = new[] { "check", "check2", "packages", "admin", "fileinfos", "ButterVersion.txt" };
        public static readonly string[] extensionBlacklist = new[] { ".butterold" };
        public static readonly string[] directoryBlacklist = new[] { "package" };

        public static bool isFileRelevant(string filePath)
        {
            var name = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath);

            // Check if the file name is in the blacklist (case-insensitive).
            if (fileBlacklist.Contains(name, StringComparer.OrdinalIgnoreCase))
                return false;

            // Check if the file extension is in the blacklist (case-insensitive).
            if (string.IsNullOrEmpty(extension) || extensionBlacklist.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return false;

            // Check if any directory in the path is in the blacklist (case-insensitive).
            string[] directories = filePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (string directory in directories.Take(directories.Length - 1))
            {
                if (directoryBlacklist.Contains(directory, StringComparer.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
    }
}
