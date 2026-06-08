using Furdeco_ChatBot.Data;
using Microsoft.EntityFrameworkCore;

namespace Furdeco_ChatBot.Service
{
    /// <summary>
    /// Periodically scans for (a) tickets whose SLA deadline has passed while
    /// still unresolved, and (b) customers who have waited in the live-chat
    /// queue past the alert threshold — raising a one-time notification for each.
    /// </summary>
    public class SlaMonitorHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly IConfiguration _config;
        private readonly ILogger<SlaMonitorHostedService> _logger;

        // How often to scan. SLA window is hours, so a 1-minute cadence is plenty.
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private static readonly string[] ClosedStatuses = { "Resolved", "Closed" };

        // Sessions we've already alerted on, so admins aren't spammed each scan.
        // In-memory by design — queue alerts are transient; a restart at worst
        // re-alerts a still-waiting customer once.
        private readonly HashSet<Guid> _queueAlerted = new();

        // Active sessions we've already flagged as awaiting a reply (cleared once
        // the agent replies / the chat ends).
        private readonly HashSet<Guid> _unansweredAlerted = new();

        public SlaMonitorHostedService(
            IServiceProvider sp,
            IConfiguration config,
            ILogger<SlaMonitorHostedService> logger)
        {
            _sp     = sp;
            _config = config;
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
                    await ScanQueueWaitAsync();
                    await ScanUnansweredAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SlaMonitor] Scan failed.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }

        /// <summary>
        /// Raises a one-time admin alert for any customer still queued beyond the
        /// configured threshold (default 5 min), so a long wait can't go unnoticed.
        /// </summary>
        private async Task ScanQueueWaitAsync()
        {
            var thresholdMin = _config.GetValue<int>("LiveChat:QueueWaitAlertMinutes", 5);
            var cutoff = DateTime.UtcNow.AddMinutes(-thresholdMin);

            using var scope = _sp.CreateScope();
            var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifs = scope.ServiceProvider.GetRequiredService<NotificationService>();

            var queued = await db.ChatSessions
                .Where(s => s.Status == "Queued")
                .Select(s => new { s.Id, s.CustomerName, s.Reference, s.QueuedAt })
                .ToListAsync();

            // Drop alerted ids that have since left the queue (picked up / abandoned).
            var currentIds = queued.Select(q => q.Id).ToHashSet();
            _queueAlerted.RemoveWhere(id => !currentIds.Contains(id));

            var raised = 0;
            foreach (var q in queued)
            {
                if (q.QueuedAt > cutoff) continue;        // not waiting long enough yet
                if (_queueAlerted.Contains(q.Id)) continue; // already alerted

                var mins = (int)(DateTime.UtcNow - q.QueuedAt).TotalMinutes;
                await notifs.CreateAsync(
                    "QueueWaitBreach",
                    $"{q.CustomerName} has waited {mins} min for an agent (ref {q.Reference}).",
                    "Admin");
                _queueAlerted.Add(q.Id);
                raised++;
            }

            if (raised > 0)
                _logger.LogInformation("[SlaMonitor] Raised {Count} queue-wait alert(s).", raised);
        }

        /// <summary>
        /// Flags Active chats where the customer's last message has gone unanswered
        /// by the agent beyond the threshold (default 5 min) — i.e. an assigned chat
        /// being neglected (including when the agent has gone offline). One alert per
        /// session until the agent replies / the chat ends.
        /// </summary>
        private async Task ScanUnansweredAsync()
        {
            var thresholdMin = _config.GetValue<int>("LiveChat:UnansweredAlertMinutes", 5);
            var now = DateTime.UtcNow;

            using var scope = _sp.CreateScope();
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionService>();
            var notifs   = scope.ServiceProvider.GetRequiredService<NotificationService>();

            var active = await sessions.GetActiveSessionsAsync();
            var ids = active.Select(s => s.Id).ToList();
            var lastMsgs = await sessions.GetLastMessagePerSessionAsync(ids);

            var currentlyStalled = new HashSet<Guid>();
            var raised = 0;

            foreach (var s in active)
            {
                if (!lastMsgs.TryGetValue(s.Id, out var lm)) continue;
                if (!lm.SenderType.Equals("Customer", StringComparison.OrdinalIgnoreCase)) continue;

                var mins = (int)(now - lm.Timestamp).TotalMinutes;
                if (mins < thresholdMin) continue;

                currentlyStalled.Add(s.Id);
                if (_unansweredAlerted.Contains(s.Id)) continue; // already alerted

                await notifs.CreateAsync(
                    "UnansweredChat",
                    $"{s.Agent?.Name ?? "The assigned agent"} hasn't replied to {s.CustomerName} for {mins} min.",
                    "Admin");
                _unansweredAlerted.Add(s.Id);
                raised++;
            }

            // Reset sessions that are no longer stalled (agent replied) or have ended.
            _unansweredAlerted.RemoveWhere(id => !currentlyStalled.Contains(id));

            if (raised > 0)
                _logger.LogInformation("[SlaMonitor] Raised {Count} unanswered-chat alert(s).", raised);
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
