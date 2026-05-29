using Furdeco_ChatBot.Models;
using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Mvc;

namespace Furdeco_ChatBot.Controllers
{
    [Route("api/dashboard")]
    [ApiController]
    public class DashboardController : ControllerBase
    {
        private readonly IApiLoggerService _logger;
        private readonly IWebHostEnvironment _env;

        public DashboardController(IApiLoggerService logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        // GET /api/dashboard/stats?date=2026-05-27
        [HttpGet("stats")]
        public IActionResult Stats([FromQuery] string? date)
        {
            var targetDate = date != null && DateTime.TryParse(date, out var d) ? d : DateTime.UtcNow.Date;
            var logs = GetLogsForDate(targetDate);
            return Ok(BuildStats(logs, targetDate));
        }

        // GET /api/dashboard/history?days=7
        [HttpGet("history")]
        public IActionResult History([FromQuery] int days = 7)
        {
            var result = new List<object>();
            for (int i = days - 1; i >= 0; i--)
            {
                var day = DateTime.UtcNow.Date.AddDays(-i);
                var logs = GetLogsForDate(day);
                result.Add(new
                {
                    date = day.ToString("yyyy-MM-dd"),
                    label = day.ToString("dd MMM"),
                    totalRequests = logs.Count,
                    trackLookups = logs.Count(l => l.Endpoint == "track"),
                    confirmations = logs.Count(l => l.Endpoint == "confirm"),
                    declines = logs.Count(l => l.Endpoint == "decline"),
                    otpSent = logs.Count(l => l.Endpoint == "send-otp"),
                    successRate = logs.Count == 0 ? 0 : Math.Round((double)logs.Count(l => l.IsSuccess) / logs.Count * 100, 1)
                });
            }
            return Ok(result);
        }

        // GET /api/dashboard/logs?date=2026-05-27&limit=200
        [HttpGet("logs")]
        public IActionResult Logs([FromQuery] string? date, [FromQuery] int limit = 200)
        {
            var targetDate = date != null && DateTime.TryParse(date, out var d) ? d : DateTime.UtcNow.Date;
            var logs = GetLogsForDate(targetDate)
                .OrderByDescending(l => l.UtcTimestamp)
                .Take(limit)
                .Select(l => new
                {
                    date = l.UtcTimestamp.ToString("dd-MMM-yyyy"),
                    time = l.UtcTimestamp.ToString("HH:mm:ss"),
                    endpoint = l.Endpoint,
                    reference = l.Reference,
                    postcode = l.Postcode,
                    refType = l.RefType,
                    otpChannel = l.OtpChannel,
                    responseMs = l.ResponseTimeMs,
                    status = l.HttpStatus,
                    isSuccess = l.IsSuccess,
                    dataFound = l.DataFound,
                    isUserInput = l.IsUserInput,
                    // Order snapshot fields
                    orderNumber = l.OrderNumber,
                    recipient = l.Recipient,
                    senderCompany = l.SenderCompany,
                    address = l.Address,
                    plannedDate = l.PlannedDate,
                    plannedSlotStart = l.PlannedSlotStart,
                    plannedSlotEnd = l.PlannedSlotEnd,
                    deliveryStatus = l.DeliveryStatus,
                    serviceLevel = l.ServiceLevel,
                    deliveryPoint = l.DeliveryPoint,
                    productDescriptions = l.ProductDescriptions
                });
            return Ok(logs);
        }

        // GET /api/dashboard/hourly?date=2026-05-27
        [HttpGet("hourly")]
        public IActionResult Hourly([FromQuery] string? date)
        {
            var targetDate = date != null && DateTime.TryParse(date, out var d) ? d : DateTime.UtcNow.Date;
            var logs = GetLogsForDate(targetDate);

            var hourly = Enumerable.Range(0, 24).Select(h => new
            {
                hour = h,
                label = $"{h:D2}:00",
                requests = logs.Count(l => l.UtcTimestamp.Hour == h),
                tracks = logs.Count(l => l.Endpoint == "track" && l.UtcTimestamp.Hour == h),
                avgMs = logs.Where(l => l.UtcTimestamp.Hour == h).Select(l => l.ResponseTimeMs)
                            .DefaultIfEmpty(0).Average()
            });
            return Ok(hourly);
        }

        // GET /api/dashboard/available-dates
        [HttpGet("available-dates")]
        public IActionResult AvailableDates()
        {
            var dates = new HashSet<string>();

            // JSONL files: api-log-yyyy-MM-dd.jsonl
            var jsonlDir = Path.Combine(_env.ContentRootPath, "Logs", "ReportLogs");
            if (Directory.Exists(jsonlDir))
            {
                foreach (var f in Directory.GetFiles(jsonlDir, "api-log-*.jsonl"))
                {
                    var d = Path.GetFileNameWithoutExtension(f).Replace("api-log-", "");
                    if (!string.IsNullOrEmpty(d)) dates.Add(d);
                }
            }

            // Serilog text files: log-yyyyMMdd.txt  (fallback source)
            var logDir = Path.Combine(_env.ContentRootPath, "Logs");
            if (Directory.Exists(logDir))
            {
                foreach (var f in Directory.GetFiles(logDir, "log-*.txt"))
                {
                    var name = Path.GetFileNameWithoutExtension(f); // "log-20260521"
                    var part = name.Length > 4 ? name[4..] : ""; // "20260521"
                    if (DateTime.TryParseExact(part, "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        dates.Add(dt.ToString("yyyy-MM-dd"));
                    }
                }
            }

            return Ok(dates.OrderByDescending(x => x).ToList());
        }

        private List<ApiRequestLog> GetLogsForDate(DateTime date)
        {
            // First try ReportLogs JSONL
            var logs = _logger.GetLogsForDate(date);

            // Fallback: parse Serilog .txt if JSONL empty
            if (logs.Count == 0)
            {
                var serilogPath = Path.Combine(_env.ContentRootPath, "Logs",
                    $"log-{date:yyyyMMdd}.txt");
                if (System.IO.File.Exists(serilogPath))
                {
                    var parser = new LogFileParserService();
                    logs = parser.ParseSerilogFile(serilogPath);
                }
            }
            return logs;
        }

        private static object BuildStats(List<ApiRequestLog> logs, DateTime date)
        {
            var tracks    = logs.Where(l => l.Endpoint == "track").ToList();
            var otpVerify = logs.Where(l => l.Endpoint == "verify-otp").ToList();
            var otpSend   = logs.Count(l => l.Endpoint == "send-otp");
            var avgMs     = logs.Count == 0 ? 0 : (long)logs.Average(l => l.ResponseTimeMs);
            var minMs     = logs.Count == 0 ? 0 : logs.Min(l => l.ResponseTimeMs);
            var maxMs     = logs.Count == 0 ? 0 : logs.Max(l => l.ResponseTimeMs);
            var otpVerifiedCount = otpVerify.Count(l => l.IsSuccess);
            var otpSuccessRate   = otpSend > 0 ? Math.Round(otpVerifiedCount * 100.0 / otpSend, 1) : 0.0;
            var peakHour  = tracks.GroupBy(l => l.UtcTimestamp.Hour)
                                  .OrderByDescending(g => g.Count())
                                  .FirstOrDefault();
            var peakLabel = peakHour != null ? $"{peakHour.Key:00}:00" : "N/A";

            return new
            {
                date = date.ToString("yyyy-MM-dd"),
                totalRequests   = logs.Count,
                trackLookups    = tracks.Count,
                ordersFound     = tracks.Count(l => l.DataFound == true),
                ordersNotFound  = tracks.Count(l => l.DataFound == false),
                confirmations   = logs.Count(l => l.Endpoint == "confirm"),
                declines        = logs.Count(l => l.Endpoint == "decline"),
                instructions    = logs.Count(l => l.Endpoint == "instructions"),
                notes           = logs.Count(l => l.Endpoint == "note"),
                otpSent         = otpSend,
                otpVerified     = otpVerifiedCount,
                otpFailed       = otpVerify.Count(l => !l.IsSuccess),
                otpSuccessRate  = otpSuccessRate,
                userInputQueries = logs.Count(l => l.IsUserInput),
                avgResponseMs   = avgMs,
                minResponseMs   = minMs,
                maxResponseMs   = maxMs,
                peakHour        = peakLabel,
                successRate     = logs.Count == 0 ? 0 : Math.Round((double)logs.Count(l => l.IsSuccess) / logs.Count * 100, 1),
                endpointBreakdown = logs.GroupBy(l => l.Endpoint)
                    .Select(g => new
                    {
                        endpoint = g.Key,
                        count    = g.Count(),
                        success  = g.Count(l => l.IsSuccess),
                        failed   = g.Count(l => !l.IsSuccess),
                        avgMs    = (long)g.Average(l => l.ResponseTimeMs),
                        minMs    = g.Min(l => l.ResponseTimeMs),
                        maxMs    = g.Max(l => l.ResponseTimeMs)
                    })
                    .OrderByDescending(x => x.count)
                    .ToList(),
                refTypeBreakdown = tracks.GroupBy(l => l.RefType ?? "unknown")
                    .Select(g => new { type = g.Key, count = g.Count() })
                    .ToList(),
                otpChannelBreakdown = logs.Where(l => l.OtpChannel != null)
                    .GroupBy(l => l.OtpChannel!)
                    .Select(g => new { channel = g.Key, count = g.Count() })
                    .ToList()
            };
        }
    }
}
