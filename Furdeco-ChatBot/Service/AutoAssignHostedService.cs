using Furdeco_ChatBot.Data;
using Furdeco_ChatBot.Hubs;
using Furdeco_ChatBot.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Furdeco_ChatBot.Service
{
    /// <summary>
    /// When auto-assign is enabled, pushes the oldest queued chat to the
    /// longest-idle online agent that's under the concurrency cap. If the agent
    /// doesn't send a first reply within the response window, the chat is
    /// reassigned to the next agent and the slow agent is flagged. After the
    /// configured number of attempts the chat is escalated to an admin and left
    /// in the queue for manual pickup.
    /// </summary>
    public class AutoAssignHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly AutoAssignTracker _tracker;
        private readonly IHubContext<LiveChatHub> _hub;
        private readonly ILogger<AutoAssignHostedService> _logger;

        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

        public AutoAssignHostedService(
            IServiceProvider sp,
            AutoAssignTracker tracker,
            IHubContext<LiveChatHub> hub,
            ILogger<AutoAssignHostedService> logger)
        {
            _sp      = sp;
            _tracker = tracker;
            _hub     = hub;
            _logger  = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[AutoAssign] Hosted service started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ScanAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AutoAssign] Scan failed.");
                }
                await Task.Delay(Interval, stoppingToken);
            }
        }

        private async Task ScanAsync()
        {
            using var scope = _sp.CreateScope();
            var db       = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionService>();
            var agentsSvc = scope.ServiceProvider.GetRequiredService<AgentUserService>();
            var notifs   = scope.ServiceProvider.GetRequiredService<NotificationService>();
            var settingsSvc = scope.ServiceProvider.GetRequiredService<SettingsService>();

            var settings = await settingsSvc.GetAsync();
            if (!settings.AutoAssignEnabled) return;

            var now     = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(settings.ResponseTimeoutSeconds);

            // Snapshot agents + current load once. A "Busy" agent is still eligible
            // up to the cap (they can hold several chats) — only Away/Offline are out.
            var eligibleAgents = (await agentsSvc.GetAllAsync())
                .Where(a => a.Status == "Online" || a.Status == "Busy")
                .ToList();

            var activeCounts = (await db.ChatSessions
                    .Where(s => s.Status == "Active" && s.AgentId != null)
                    .GroupBy(s => s.AgentId!.Value)
                    .Select(g => new { AgentId = g.Key, Count = g.Count() })
                    .ToListAsync())
                .ToDictionary(x => x.AgentId, x => x.Count);

            // Is there another agent (not the excluded one) who could take this chat?
            bool HasAlternative(Guid sessionId, Guid excludeAgentId) =>
                eligibleAgents.Any(a => a.Id != excludeAgentId
                    && activeCounts.GetValueOrDefault(a.Id) < settings.MaxConcurrentChats
                    && !_tracker.HasTried(sessionId, a.Id));

            // ── 1) Handle pending offers that have timed out ───────────────────
            foreach (var sid in _tracker.TrackedSessions())
            {
                var offer = _tracker.GetOffer(sid);
                if (offer == null) continue;

                if (offer.Replied)
                {
                    _tracker.Forget(sid); // responded — success
                    continue;
                }

                if (now - offer.OfferedAt < timeout) continue; // still within window

                // Timed out. Only reassign if there's genuinely somewhere better to
                // send it — otherwise keep it with the current agent (don't orphan,
                // don't unfairly flag an agent who's simply busy with no backup).
                if (!HasAlternative(sid, offer.AgentId)) continue;

                var agent = await agentsSvc.GetByIdAsync(offer.AgentId);
                var session = await db.ChatSessions.FindAsync(sid);

                if (session != null && session.Status == "Active" && session.AgentId == offer.AgentId)
                {
                    session.Status     = "Queued";
                    session.AgentId    = null;
                    session.AcceptedAt = null;
                    db.ChatMessages.Add(new ChatMessage
                    {
                        SessionId  = sid,
                        SenderType = "System",
                        SenderName = "System",
                        Content    = "Connecting you to another agent…",
                        Timestamp  = now
                    });
                    await db.SaveChangesAsync();

                    activeCounts[offer.AgentId] = Math.Max(0, activeCounts.GetValueOrDefault(offer.AgentId) - 1);
                    await agentsSvc.UpdateStatusAsync(offer.AgentId,
                        activeCounts.GetValueOrDefault(offer.AgentId) == 0 ? "Online" : "Busy");
                }

                await notifs.CreateAsync(
                    "AssignTimeout",
                    $"{agent?.Name ?? "An agent"} didn't respond to {session?.CustomerName ?? "a customer"} within {settings.ResponseTimeoutSeconds}s — reassigning.",
                    "Admin");

                await _hub.Clients.Group($"agent:{offer.AgentId}").SendAsync("ChatReassigned", sid);
                _tracker.ClearOffer(sid);
                await BroadcastQueueRefresh();
            }

            // ── 2) Make new offers — at most ONE pending offer per agent ───────
            var queued = await db.ChatSessions
                .Where(s => s.Status == "Queued")
                .OrderBy(s => s.QueuedAt)
                .Select(s => new { s.Id, s.CustomerName })
                .ToListAsync();

            // Agents already holding an unanswered offer can't be offered another.
            var pendingAgents = _tracker.PendingOfferAgentIds();

            foreach (var q in queued)
            {
                if (_tracker.IsEscalated(q.Id)) continue;
                if (_tracker.GetOffer(q.Id) != null) continue; // already offered, awaiting reply

                var pick = eligibleAgents
                    .Where(a => activeCounts.GetValueOrDefault(a.Id) < settings.MaxConcurrentChats
                                && !pendingAgents.Contains(a.Id)        // one pending offer per agent
                                && !_tracker.HasTried(q.Id, a.Id))
                    .OrderBy(a => _tracker.LastAssigned(a.Id))          // longest-idle first
                    .FirstOrDefault();

                if (pick == null)
                {
                    // Escalate only when EVERY eligible agent has already been tried
                    // (genuinely ignored) — not merely because they're busy/pending.
                    var allTried = eligibleAgents.Count > 0
                        && eligibleAgents.All(a => _tracker.HasTried(q.Id, a.Id));
                    if (allTried && _tracker.TriedCount(q.Id) >= settings.MaxAssignAttempts
                        && !_tracker.IsEscalated(q.Id))
                    {
                        _tracker.Escalate(q.Id);
                        await notifs.CreateAsync(
                            "AutoAssignEscalation",
                            $"No agent responded to {q.CustomerName} after {settings.MaxAssignAttempts} attempts — needs manual pickup.",
                            "Admin");
                    }
                    continue; // otherwise the chat simply waits for an agent to free up
                }

                await sessions.AssignToAgentAsync(q.Id, pick.Id, pick.Name);
                activeCounts[pick.Id] = activeCounts.GetValueOrDefault(pick.Id) + 1;
                pendingAgents.Add(pick.Id); // block a second offer to them this cycle
                _tracker.CreateOffer(q.Id, pick.Id, now);

                await _hub.Clients.Group($"session:{q.Id}").SendAsync("AgentJoined", pick.Name);
                await _hub.Clients.Group($"agent:{pick.Id}")
                    .SendAsync("ChatAutoAssigned", new { sessionId = q.Id, customerName = q.CustomerName });

                await agentsSvc.UpdateStatusAsync(pick.Id, "Busy");
                await _hub.Clients.Group("admins").SendAsync("AgentStatusChanged", pick.Id, "Busy");
                await BroadcastQueueRefresh();

                _logger.LogInformation("[AutoAssign] Offered {Session} to {Agent}.", q.Id, pick.Name);
            }

            // Forget tracker entries for sessions that are no longer queued/active.
            var liveIds = (await db.ChatSessions
                    .Where(s => s.Status == "Queued" || s.Status == "Active")
                    .Select(s => s.Id)
                    .ToListAsync())
                .ToHashSet();
            CleanupTracker(liveIds);
        }

        private void CleanupTracker(HashSet<Guid> liveIds)
        {
            foreach (var sid in _tracker.TrackedSessions())
                if (!liveIds.Contains(sid))
                    _tracker.Forget(sid);
        }

        private async Task BroadcastQueueRefresh()
        {
            // No payload → clients refetch queue + sessions.
            await _hub.Clients.Group("agents").SendAsync("QueueUpdated");
            await _hub.Clients.Group("admins").SendAsync("QueueUpdated");
        }
    }
}
