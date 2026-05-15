using Modern_MHFZ_PatchServer.logger;
using Newtonsoft.Json.Linq;
using PatchServer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Modern_MHFZ_PatchServer.utils
{
    internal class FileManifest
    {
        // Generates a manifest string.
        public static string buildManifest(ConcurrentDictionary<string, fileDescriptor> files, int precision = 2)
        {
            // Estimate the size of each line based on the precision level to optimize StringBuilder capacity.
            int estimatedLineSize = precision switch
            {
                1 => 64 + 1,
                2 => 64 + 1 + 512 + 1,
                3 => 64 + 1 + 512 + 1 + 19 + 1,
                4 => 64 + 1 + 512 + 1 + 19 + 1 + 19 + 1,
                _ => 64 + 1 + 512 + 1
            };
            var sb = new StringBuilder(capacity: files.Count * estimatedLineSize);

            foreach (var file in files)
            {
                sb.Append(file.Value.hash); // Hash is always included.
                if (precision >= 2)
                    sb.Append('\t' + file.Key); // Path is included at precision 2 and above.
                if (precision >= 3)
                    sb.Append('\t' + file.Value.size.ToString(CultureInfo.InvariantCulture)); // Size is included at precision 3 and above.
                if (precision >= 4)
                    sb.Append('\t' + file.Value.edit.ToString(CultureInfo.InvariantCulture)); // Edit time is included at precision 4 and above.
                sb.Append('\n');
            }

            return sb.ToString();
        }
        // Reads the manifest file with a precision level of 4 and returns an array of fileDescriptor objects.
        public static fileDescriptor[] readFileinfo(string path)
        {
            if(!File.Exists(Path.Combine(path, "fileinfos")))
                return Array.Empty<fileDescriptor>();

            try
            {
                var text = File.ReadAllLines(Path.Combine(path, "fileinfos"));
                List<fileDescriptor> descriptors = new List<fileDescriptor>();

                // Each line is expected to be in the format: hash\tpath\tsize\tedit
                foreach (var line in text)
                {
                    var parts = line.Split('\t');
                    if (parts.Length != 4) continue;
                    descriptors.Add(new fileDescriptor
                    {
                        hash = parts[0],
                        path = parts[1],
                        size = long.Parse(parts[2], CultureInfo.InvariantCulture),
                        edit = long.Parse(parts[3], CultureInfo.InvariantCulture)
                    });
                }

                return descriptors.ToArray();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to read fileinfos: {ex.Message}, rebuilding...", "FileManifest");
            }
            return Array.Empty<fileDescriptor>();
        }
    }
}
