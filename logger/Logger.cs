using Modern_MHFZ_PatchServer.utils;
using System.Globalization;
using System.Text;

namespace Modern_MHFZ_PatchServer.logger
{
    public static class Logger
    {
        // Locks to ensure thread safety when writing to console and file.
        private static readonly object ConsoleLock = new();
        private static readonly object FileLock = new();

        // Padding for module ID in console output to ensure alignment.
        private static readonly int ModulePadding = 20;

        private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
        private static readonly string fileName = $"patchserver-{DateTimeOffset.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}.log";

        // Default console colors to restore after logging.
        private static ConsoleColor defaultForeground = ConsoleColor.White;
        private static ConsoleColor defaultBackground = ConsoleColor.Black;

        public static void LogInfo(string message, string moduleID, bool skipLogFile = false) => SimpleLog(message, moduleID, skipLogFile, foregroundColor: ConsoleColor.White, backgroundColor: ConsoleColor.Black);
        public static void LogDebug(string message, string moduleID, bool skipLogFile = false) { if (Config.options.Logger.Debug) SimpleLog(message, moduleID, skipLogFile, foregroundColor: Config.options.Logger.PuTTYMode ? ConsoleColor.Green : ConsoleColor.DarkGray, backgroundColor: ConsoleColor.Black); }
        public static void LogError(string message, string moduleID, bool skipLogFile = false) => SimpleLog(message, moduleID, skipLogFile, foregroundColor: ConsoleColor.Red, backgroundColor: ConsoleColor.Black);
        public static void LogWarning(string message, string moduleID, bool skipLogFile = false) => SimpleLog(message, moduleID, skipLogFile, foregroundColor: ConsoleColor.Yellow, backgroundColor: ConsoleColor.Black);
        public static void LogFatal(string message, string moduleID, bool skipLogFile = false) => SimpleLog(message, moduleID, skipLogFile, foregroundColor: ConsoleColor.Magenta, backgroundColor: ConsoleColor.Black);

        // Simple Log helper.
        public static void SimpleLog(string message, string moduleID, bool skipLogFile = false, ConsoleColor foregroundColor = ConsoleColor.White, ConsoleColor backgroundColor = ConsoleColor.Black)
        {
            lock (ConsoleLock)
            {
                Console.ForegroundColor = defaultForeground;
                Console.BackgroundColor = defaultBackground;
                try
                {
                    Console.ForegroundColor = foregroundColor;
                    Console.BackgroundColor = backgroundColor;

                    string module = $"[{(string.IsNullOrWhiteSpace(moduleID) ? "Unknown" : moduleID)}]".PadRight(ModulePadding);
                    string safeMessage = message ?? string.Empty;

                    Console.WriteLine($"[{moduleID}]\t{safeMessage}");

                    if (Config.options.Logger.WriteLog && !skipLogFile)
                        WriteLog(safeMessage, module);
                }
                finally
                {
                    Console.ForegroundColor = defaultForeground;
                    Console.BackgroundColor = defaultBackground;
                }
            }
        }

        // Advanced Log helper.
        public static void AdvancedLog(string message, string moduleID, bool skipLogFile = false, ConsoleColor foregroundTextColor = ConsoleColor.White, ConsoleColor foregroundDateTimeColor = ConsoleColor.White, ConsoleColor foregroundModuleIdColor = ConsoleColor.White, ConsoleColor backgroundTextColor = ConsoleColor.Black, ConsoleColor backgroundDateTimeColor = ConsoleColor.Black, ConsoleColor backgroundModuleIdColor = ConsoleColor.Black)
        {
            lock (ConsoleLock)
            {
                Console.ForegroundColor = defaultForeground;
                Console.BackgroundColor = defaultBackground;
                try
                {
                    Console.ForegroundColor = foregroundModuleIdColor;
                    Console.BackgroundColor = backgroundModuleIdColor;
                    string module = $"[{(string.IsNullOrWhiteSpace(moduleID) ? "Unknown" : moduleID)}]".PadRight(ModulePadding);
                    Console.Write($"{module}");

                    Console.ForegroundColor = foregroundTextColor;
                    Console.BackgroundColor = backgroundTextColor;
                    string safeMessage = message ?? string.Empty;
                    Console.WriteLine(safeMessage);

                    if (Config.options.Logger.WriteLog && !skipLogFile)
                        WriteLog(safeMessage, module);
                }
                finally
                {
                    Console.ForegroundColor = defaultForeground;
                    Console.BackgroundColor = defaultBackground;
                }
            }
        }

        // Append to the log file.
        public static void WriteLog(string message, string module)
        {
            string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);

            string filePath = Path.Combine(LogDirectory, fileName);

            lock (FileLock)
            {
                try
                {
                    Directory.CreateDirectory(LogDirectory);
                    
                    using FileStream stream = new( filePath, FileMode.Append, FileAccess.Write, FileShare.Read);

                    using StreamWriter writer = new( stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                    writer.Write($"{timestamp}\t{module}{message}\n");
                }
                catch (Exception ex)
                {
                    LogError($"Failed to write log to file: {ex.Message}", "Logger", true);
                }
            }
        }
    }
}
