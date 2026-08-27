using System.Net;
using System.Net.Mail;

namespace Furdeco_ChatBot.Service
{
    /// <summary>
    /// Background service that fires once a day at the configured UK time,
    /// generates an Excel activity report for the previous day, and emails it.
    /// </summary>
    public class DailyReportHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly IConfiguration _config;
        private readonly ILogger<DailyReportHostedService> _logger;

        private static readonly TimeZoneInfo UkTz =
            TimeZoneInfo.FindSystemTimeZoneById(
                System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows)
                ? "GMT Standard Time"   // Windows (local dev, Windows App Service)
                : "Europe/London");     // Linux (Azure App Service Linux)

        public DailyReportHostedService(
            IServiceProvider sp,
            IConfiguration config,
            ILogger<DailyReportHostedService> logger)
        {
            _sp     = sp;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[DailyReport] Hosted service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeUntilNextRun();
                _logger.LogInformation("[DailyReport] Next report in {Hours}h {Min}m.",
                    (int)delay.TotalHours, delay.Minutes);

                await Task.Delay(delay, stoppingToken);

                if (stoppingToken.IsCancellationRequested) break;

                await GenerateAndSendReportAsync();
            }
        }

        /// <summary>Calculates how long until the next configured send time (UK clock).</summary>
        private TimeSpan TimeUntilNextRun()
        {
            // Configurable send hour in UK time — default 07:00
            var sendHour   = _config.GetValue<int>("DailyReport:SendHourUK", 7);
            var sendMinute = _config.GetValue<int>("DailyReport:SendMinuteUK", 0);

            var nowUtc  = DateTime.UtcNow;
            var nowUk   = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, UkTz);

            var todayTarget = new DateTime(nowUk.Year, nowUk.Month, nowUk.Day,
                sendHour, sendMinute, 0, DateTimeKind.Unspecified);

            var targetUk = todayTarget > nowUk ? todayTarget : todayTarget.AddDays(1);

            // Convert target back to UTC for accurate delay calculation
            var targetUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(targetUk, DateTimeKind.Unspecified), UkTz);

            return targetUtc - nowUtc;
        }

        private async Task GenerateAndSendReportAsync()
        {
            try
            {
                // Report covers YESTERDAY in UK time
                var yesterdayUtc = DateTime.UtcNow.Date.AddDays(-1);

                _logger.LogInformation("[DailyReport] Generating report for {Date:yyyy-MM-dd}.", yesterdayUtc);

                using var scope = _sp.CreateScope();
                var loggerSvc  = scope.ServiceProvider.GetRequiredService<IApiLoggerService>();
                var excelSvc   = scope.ServiceProvider.GetRequiredService<ExcelReportService>();

                var logs = loggerSvc.GetLogsForDate(yesterdayUtc);
                _logger.LogInformation("[DailyReport] {Count} log entries found.", logs.Count);

                var excelBytes = excelSvc.Generate(logs, yesterdayUtc);

                var recipient  = _config["DailyReport:RecipientEmail"] ?? "atripathi@zouma.ai";
                var dateLabel  = TimeZoneInfo
                    .ConvertTimeFromUtc(yesterdayUtc, UkTz)
                    .ToString("dd-MMM-yyyy");

                await SendEmailAsync(recipient, excelBytes, dateLabel, logs.Count);

                _logger.LogInformation("[DailyReport] Report sent to {Email} for {Date}.", recipient, dateLabel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DailyReport] Failed to generate or send report.");
            }
        }

        private async Task SendEmailAsync(string toEmail, byte[] excelBytes,
            string dateLabel, int logCount)
        {
            var smtp     = _config["EmailSettings:SmtpServer"] ?? "smtp.zeptomail.in";
            var port     = int.Parse(_config["EmailSettings:Port"] ?? "587");
            var username = _config["EmailSettings:Username"] ?? "";
            var password = _config["EmailSettings:Password"] ?? "";
            // Sender is its own setting - see the note in ChatController: the
            // ZeptoMail username is "emailapikey", not an address.
            var fromEmail = _config["EmailSettings:FromEmail"] ?? username;

            using var client = new SmtpClient(smtp, port)
            {
                EnableSsl   = true,
                Credentials = new NetworkCredential(username, password)
            };

            var fileName = $"Furdeco-ChatBot-Report-{dateLabel}.xlsx";

            var mail = new MailMessage
            {
                From       = new MailAddress(fromEmail, "Ask Frankie by Furdeco"),
                Subject    = $"[Furdeco ChatBot] Daily Activity Report — {dateLabel}",
                IsBodyHtml = true,
                Body       = BuildEmailBody(dateLabel, logCount)
            };

            mail.To.Add(toEmail);

            using var ms = new MemoryStream(excelBytes);
            mail.Attachments.Add(new Attachment(ms, fileName,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

            await client.SendMailAsync(mail);
        }

        private static string BuildEmailBody(string dateLabel, int logCount) => $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f5f5f7;font-family:""DM Sans"",Arial,sans-serif'>
  <table width='100%' cellpadding='0' cellspacing='0' style='padding:40px 0'>
    <tr><td align='center'>
      <table width='520' cellpadding='0' cellspacing='0'
             style='background:white;border-radius:16px;overflow:hidden;
                    box-shadow:0 4px 24px rgba(0,0,0,0.08)'>
        <tr>
          <td style='background:linear-gradient(135deg,#2a8f38,#3AB54A);
                     padding:28px 40px;text-align:center'>
            <div style='font-size:28px;margin-bottom:6px'>📊</div>
            <div style='font-size:20px;font-weight:800;color:white'>
              Ask Frankie by Furdeco
            </div>
            <div style='font-size:12px;color:rgba(255,255,255,0.8);margin-top:4px'>
              Daily Activity Report
            </div>
          </td>
        </tr>
        <tr>
          <td style='padding:32px 40px'>
            <p style='font-size:15px;color:#1c1c1e;margin:0 0 16px'>
              Hi,<br><br>
              Please find attached the <strong>Furdeco ChatBot activity report</strong>
              for <strong>{dateLabel}</strong>.
            </p>
            <table width='100%' cellpadding='8' cellspacing='0'
                   style='background:#f0faf1;border-radius:10px;margin:16px 0'>
              <tr>
                <td style='font-size:13px;color:#444'>Total API Calls Logged</td>
                <td style='font-size:16px;font-weight:800;color:#2a8f38;
                           text-align:right'>{logCount:N0}</td>
              </tr>
              <tr>
                <td style='font-size:13px;color:#444'>Report Period</td>
                <td style='font-size:13px;font-weight:600;color:#1c1c1e;
                           text-align:right'>{dateLabel} (UK Time)</td>
              </tr>
            </table>
            <p style='font-size:13px;color:#8e8e93;margin:16px 0 0'>
              The attached Excel workbook contains:<br>
              &bull; <strong>Dashboard</strong> — key metrics &amp; summary<br>
              &bull; <strong>Detail Log</strong> — every API call with reference, postcode &amp; response time<br>
              &bull; <strong>Endpoint Breakdown</strong> — per-endpoint success rates<br>
              &bull; <strong>Hourly Breakdown</strong> — activity by hour (UK time)
            </p>
          </td>
        </tr>
        <tr>
          <td style='background:#f5f5f7;padding:14px 40px;text-align:center;
                     font-size:11px;color:#8e8e93'>
            © 2026 Furdeco · Automated Report · Do not reply to this email
          </td>
        </tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";
    }
}
