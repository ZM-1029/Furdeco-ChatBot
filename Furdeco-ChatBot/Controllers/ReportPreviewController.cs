using Furdeco_ChatBot.Models;
using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Mvc;

namespace Furdeco_ChatBot.Controllers
{
    /// <summary>
    /// Temporary preview endpoint — remove before production if desired.
    /// GET /api/report/preview  → downloads a sample Excel report with dummy data.
    /// </summary>
    [Route("api/report")]
    [ApiController]
    public class ReportPreviewController : ControllerBase
    {
        private readonly ExcelReportService _excel;

        public ReportPreviewController(ExcelReportService excel)
        {
            _excel = excel;
        }

        /// <summary>
        /// GET /api/report/from-log?path=D:\log-20260518.txt
        /// Parses a Serilog log file and returns a real Excel report for that day.
        /// </summary>
        [HttpGet("from-log")]
        public IActionResult FromLog([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("Query param 'path' is required. Example: /api/report/from-log?path=D:\\log-20260518.txt");

            if (!System.IO.File.Exists(path))
                return NotFound($"File not found: {path}");

            var parser = new LogFileParserService();
            var logs   = parser.ParseSerilogFile(path);

            if (logs.Count == 0)
                return NotFound($"No /api/chat/* request lines found in: {path}");

            var reportDate = logs.Min(l => l.UtcTimestamp).Date;
            var bytes      = _excel.Generate(logs, reportDate);
            var dateLabel  = reportDate.ToString("dd-MMM-yyyy");
            var fileName   = $"Furdeco-Report-{dateLabel}.xlsx";

            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        [HttpGet("preview")]
        public IActionResult Preview()
        {
            var logs = GenerateDummyLogs();
            var bytes = _excel.Generate(logs, DateTime.UtcNow.Date.AddDays(-1));
            var fileName = $"Furdeco-Report-Preview-{DateTime.UtcNow:dd-MMM-yyyy}.xlsx";
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // ── Dummy data generator ─────────────────────────────────────────────
        private static List<ApiRequestLog> GenerateDummyLogs()
        {
            var rng  = new Random(42);
            var logs = new List<ApiRequestLog>();
            var refs = new[]
            {
                "FRD-2024-001234", "FRD-2024-005678", "FRD-2024-009012",
                "ORD-78432", "ORD-91234", "ORD-55678",
                "CN-887732", "CN-442211", "CN-998801",
                "DO I NEED TO SIGN FOR MY DELIVERY",
                "WHERE IS MY ORDER", "HELLO CAN YOU HELP ME"
            };
            var postcodes = new[] { "B1 1AA", "M1 1AE", "E1 6AN", "SW1A 1AA", "LS1 1BA", "CF10 1AA" };
            var endpoints = new[] { "track", "track", "track", "track", "confirm", "decline",
                                    "instructions", "note", "send-otp", "verify-otp" };

            // Yesterday spread over 24 hours UK time
            var baseDate = DateTime.UtcNow.Date.AddDays(-1);

            for (int i = 0; i < 300; i++)
            {
                var ep    = endpoints[rng.Next(endpoints.Length)];
                var hour  = rng.Next(8, 21); // 8am–9pm activity
                var mins  = rng.Next(0, 60);
                var secs  = rng.Next(0, 60);
                var ts    = baseDate.AddHours(hour).AddMinutes(mins).AddSeconds(secs);

                var refVal   = refs[rng.Next(refs.Length)];
                var isUserInput = refVal.Contains(' ');
                var isTrack  = ep == "track";
                var dataFound = isTrack ? (isUserInput ? false : rng.Next(100) < 65) : (bool?)null;
                var refType  = isTrack ? (refVal.StartsWith("ORD") ? "order" : "consignment") : null;
                var respMs   = rng.Next(80, 1400);
                var isOtp    = ep is "send-otp" or "verify-otp";
                var otpChan  = isOtp ? (rng.Next(2) == 0 ? "phone" : "email") : null;
                var success  = ep == "verify-otp" ? rng.Next(100) < 98 : true;

                logs.Add(new ApiRequestLog
                {
                    UtcTimestamp   = ts,
                    Endpoint       = ep,
                    Reference      = isTrack || ep is "confirm" or "decline" or "instructions" or "note"
                                       ? refVal : null,
                    Postcode       = isTrack ? postcodes[rng.Next(postcodes.Length)] : null,
                    RefType        = refType,
                    OtpChannel     = otpChan,
                    ResponseTimeMs = respMs,
                    HttpStatus     = success ? 200 : 400,
                    IsSuccess      = success,
                    DataFound      = dataFound,
                    IsUserInput    = isUserInput
                });
            }

            return logs.OrderBy(l => l.UtcTimestamp).ToList();
        }
    }
}
