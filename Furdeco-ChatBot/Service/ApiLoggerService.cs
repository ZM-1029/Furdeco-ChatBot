using Furdeco_ChatBot.Models;
using System.Text.Json;

namespace Furdeco_ChatBot.Service
{
    public interface IApiLoggerService
    {
        void Log(ApiRequestLog entry);
        List<ApiRequestLog> GetLogsForDate(DateTime utcDate);
    }

    public class ApiLoggerService : IApiLoggerService
    {
        private readonly string _logDirectory;
        private readonly object _fileLock = new();

        public ApiLoggerService(IWebHostEnvironment env)
        {
            _logDirectory = Path.Combine(env.ContentRootPath, "ReportLogs");
            Directory.CreateDirectory(_logDirectory);
        }

        private string FilePath(DateTime utcDate) =>
            Path.Combine(_logDirectory, $"api-log-{utcDate:yyyy-MM-dd}.jsonl");

        public void Log(ApiRequestLog entry)
        {
            var line = JsonSerializer.Serialize(entry);
            lock (_fileLock)
            {
                File.AppendAllText(FilePath(entry.UtcTimestamp.Date), line + Environment.NewLine);
            }
        }

        public List<ApiRequestLog> GetLogsForDate(DateTime utcDate)
        {
            var path = FilePath(utcDate.Date);
            if (!File.Exists(path)) return new List<ApiRequestLog>();

            var logs = new List<ApiRequestLog>();
            lock (_fileLock)
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<ApiRequestLog>(line);
                        if (entry != null) logs.Add(entry);
                    }
                    catch { /* skip malformed lines */ }
                }
            }
            return logs;
        }
    }
}
