using Microsoft.Extensions.Logging;

namespace RaceCorProDrive.Support
{
    /// <summary>
    /// Simple logger wrapper using Microsoft.Extensions.Logging.Debug.
    /// Convenience static methods (Info / Warn / Error) forward to a single
    /// process-wide logger; for component-scoped logs, prefer
    /// <see cref="CreateLogger"/> / <see cref="CreateLogger{T}"/>.
    /// </summary>
    public static class AppLogger
    {
        private static readonly ILoggerFactory _factory = LoggerFactory.Create(builder =>
        {
            builder.AddDebug();
        });

        private static readonly ILogger _default = _factory.CreateLogger("RaceCorProDrive");

        public static ILogger CreateLogger(string categoryName)
        {
            return _factory.CreateLogger(categoryName);
        }

        public static ILogger CreateLogger<T>()
        {
            return _factory.CreateLogger<T>();
        }

        // ── Convenience forwarders to the default logger ──────────────
        public static void Info(string message) => _default.LogInformation(message);
        public static void Warn(string message) => _default.LogWarning(message);
        public static void Error(string message) => _default.LogError(message);
    }
}
