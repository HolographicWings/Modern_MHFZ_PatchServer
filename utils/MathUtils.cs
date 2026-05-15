using System;
using System.Collections.Generic;
using System.Text;

namespace Modern_MHFZ_PatchServer.utils
{
    internal class MathUtils
    {// Parses a human-readable size string (e.g., "64K", "10M", "1G") into a long.
        public static long ParseSizeToBytes(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value is empty.", nameof(value));

            value = value.Trim();

            char last = value[^1];

            long multiplier = char.ToUpperInvariant(last) switch
            {
                'K' => 1024L,
                'M' => 1024L * 1024L,
                'G' => 1024L * 1024L * 1024L,
                'T' => 1024L * 1024L * 1024L * 1024L,
                _ => 1L
            };

            string numberPart = char.IsLetter(last)
                ? value[..^1]
                : value;

            if (!long.TryParse(numberPart, out long number))
                throw new FormatException($"Invalid size value: {value}");

            checked
            {
                return number * multiplier;
            }
        }
        // Parses a human-readable size string (e.g., "64K", "10M", "1G") into an int.
        public static int ParseSizeToBytesInt(string value)
        {
            long bytes = ParseSizeToBytes(value);

            if (bytes > int.MaxValue)
                throw new OverflowException($"Value is too large for int: {value}");

            return (int)bytes;
        }
    }
}
