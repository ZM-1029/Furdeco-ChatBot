using Furdeco_ChatBot.Data;
using Microsoft.EntityFrameworkCore;

namespace Furdeco_ChatBot.Service
{
    /// <summary>
    /// Periodically scans for tickets whose SLA deadline has passed while still
    /// unresolved, and raises a one-time "SLABreach" notification for each.
    /// </summary>
    public class SlaMonitorHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<SlaMonitorHostedService> _logger;

        // How often to scan. SLA window is hours, so a 1-minute cadence is plenty.
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private static readonly string[] ClosedStatuses = { "Resolved", "Closed" };

        public SlaMonitorHostedService(IServiceProvider sp, ILogger<SlaMonitorHostedService> logger)
        {
            _sp     = sp;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[SlaMonitor] Hosted service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ScanOnceAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SlaMonitor] Scan failed.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }

        private async Task ScanOnceAsync()
        {
            var now = DateTime.UtcNow;

            using var scope = _sp.CreateScope();
            var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifs = scope.ServiceProvider.GetRequiredService<NotificationService>();

            var breached = await db.Tickets
                .Where(t => !t.SlaBreachNotified
                            && t.SlaDeadline < now
                            && !ClosedStatuses.Contains(t.Status))
                .ToListAsync();

            if (breached.Count == 0) return;

            foreach (var t in breached)
            {
                t.SlaBreachNotified = true;
                await notifs.CreateAsync(
                    "SLABreach",
                    $"SLA breached for ticket {t.Reference} — {t.CustomerName}",
                    "Admin");
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("[SlaMonitor] Raised {Count} SLA-breach notification(s).", breached.Count);
        }
    }
}
