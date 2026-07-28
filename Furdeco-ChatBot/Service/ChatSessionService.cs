using Furdeco_ChatBot.Data;
using Furdeco_ChatBot.Models;
using Microsoft.EntityFrameworkCore;

namespace Furdeco_ChatBot.Service
{
    public class ChatSessionService
    {
        private readonly AppDbContext _db;

        public ChatSessionService(AppDbContext db) => _db = db;

        // ── Queue ────────────────────────────────────────────────────

        public async Task<ChatSession> CreateAndQueueAsync(string reference, string customerName, string issueDescription, string? orderSnapshot = null)
        {
            var session = new ChatSession
            {
                Reference        = reference,
                CustomerName     = customerName,
                IssueDescription = issueDescription,
                OrderSnapshot    = string.IsNullOrWhiteSpace(orderSnapshot) ? null : orderSnapshot,
                Status           = "Queued",
                QueuedAt         = DateTime.UtcNow
            };
            _db.ChatSessions.Add(session);

            _db.ChatMessages.Add(new ChatMessage
            {
                SessionId  = session.Id,
                SenderType = "System",
                SenderName = "System",
                Content    = $"Chat started by {customerName} re {reference}"
            });

            await _db.SaveChangesAsync();
            return session;
        }

        public async Task<int> GetQueuePositionAsync(Guid sessionId)
        {
            var session = await _db.ChatSessions.FindAsync(sessionId);
            if (session == null) return 0;
            return await _db.ChatSessions
                .Where(s => s.Status == "Queued" && s.QueuedAt <= session.QueuedAt)
                .CountAsync();
        }

        public async Task<List<ChatSession>> GetQueueAsync()
            => await _db.ChatSessions
                .Where(s => s.Status == "Queued")
                .OrderBy(s => s.QueuedAt)
                .ToListAsync();

        /// <summary>
        /// Dequeues the next session using FOR UPDATE SKIP LOCKED to prevent
        /// two agents grabbing the same session simultaneously.
        /// </summary>
        public async Task<ChatSession?> DequeueNextAsync()
        {
            // Raw SQL for PostgreSQL advisory lock pattern
            var session = await _db.ChatSessions
                .FromSqlRaw(@"SELECT * FROM ""ChatSessions""
                              WHERE ""Status"" = 'Queued'
                              ORDER BY ""QueuedAt""
                              LIMIT 1
                              FOR UPDATE SKIP LOCKED")
                .FirstOrDefaultAsync();
            return session;
        }

        /// <summary>
        /// Claims a SPECIFIC queued session (cherry-pick). Returns the session
        /// only if it is still Queued and not locked by another agent's claim;
        /// otherwise null (already taken / being taken). Same FOR UPDATE SKIP
        /// LOCKED guard as DequeueNextAsync to prevent double-assignment.
        /// </summary>
        public async Task<ChatSession?> ClaimSessionAsync(Guid sessionId)
        {
            var session = await _db.ChatSessions
                .FromSqlRaw(@"SELECT * FROM ""ChatSessions""
                              WHERE ""Id"" = {0} AND ""Status"" = 'Queued'
                              LIMIT 1
                              FOR UPDATE SKIP LOCKED", sessionId)
                .FirstOrDefaultAsync();
            return session;
        }

        // ── Session lifecycle ────────────────────────────────────────

        public async Task<ChatSession?> AssignToAgentAsync(Guid sessionId, int agentId, string agentName)
        {
            var session = await _db.ChatSessions.FindAsync(sessionId);
            if (session == null) return null;

            session.AgentId     = agentId;
            session.AgentName   = agentName;
            session.Status      = "Active";
            session.AcceptedAt  = DateTime.UtcNow;

            _db.ChatMessages.Add(new ChatMessage
            {
                SessionId  = sessionId,
                SenderType = "System",
                SenderName = "System",
                Content    = $"{agentName} joined the chat"
            });

            await _db.SaveChangesAsync();
            return session;
        }

        public async Task<ChatSession?> ResolveAsync(Guid sessionId, string? agentNotes, string? chatType = null)
        {
            var session = await _db.ChatSessions.FindAsync(sessionId);
            if (session == null) return null;

            session.Status     = "Resolved";
            session.ResolvedAt = DateTime.UtcNow;
            session.AgentNotes = agentNotes;
            if (!string.IsNullOrWhiteSpace(chatType))
                session.ChatType = chatType.Trim();

            _db.ChatMessages.Add(new ChatMessage
            {
                SessionId  = sessionId,
                SenderType = "System",
                SenderName = "System",
                Content    = "Chat ended"
            });

            await _db.SaveChangesAsync();
            return session;
        }

        public async Task<ChatSession?> AbandonAsync(Guid sessionId)
        {
            var session = await _db.ChatSessions.FindAsync(sessionId);
            if (session == null) return null;

            session.Status     = "Abandoned";
            session.ResolvedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return session;
        }

        public async Task<ChatSession?> TransferAsync(Guid sessionId, int newAgentId, string newAgentName)
        {
            var session = await _db.ChatSessions.FindAsync(sessionId);
            if (session == null) return null;

            session.AgentId   = newAgentId;
            session.AgentName = newAgentName;
            _db.ChatMessages.Add(new ChatMessage
            {
                SessionId  = sessionId,
                SenderType = "System",
                SenderName = "System",
                Content    = $"Chat transferred to {newAgentName}"
            });

            await _db.SaveChangesAsync();
            return session;
        }

        public async Task RateAsync(Guid sessionId, int rating)
        {
            var session = await _db.ChatSessions.FindAsync(sessionId);
            if (session == null) return;
            session.CustomerRating = rating;
            await _db.SaveChangesAsync();
        }

        // ── Messages ─────────────────────────────────────────────────

        public async Task<ChatMessage> AddMessageAsync(Guid sessionId, string senderType,
            string senderName, string content, bool isWhisper = false)
        {
            var msg = new ChatMessage
            {
                SessionId  = sessionId,
                SenderType = senderType,
                SenderName = senderName,
                Content    = content,
                IsWhisper  = isWhisper,
                Timestamp  = DateTime.UtcNow
            };
            _db.ChatMessages.Add(msg);
            await _db.SaveChangesAsync();
            return msg;
        }

        // ── Queries ──────────────────────────────────────────────────

        public async Task<ChatSession?> GetSessionAsync(Guid sessionId)
            => await _db.ChatSessions
                .Include(s => s.Messages.OrderBy(m => m.Timestamp))
                .FirstOrDefaultAsync(s => s.Id == sessionId);

        public async Task<List<ChatSession>> GetActiveSessionsAsync()
            => await _db.ChatSessions
                .Where(s => s.Status == "Active")
                .OrderBy(s => s.AcceptedAt)
                .ToListAsync();

        /// <summary>
        /// For each given session, the latest non-whisper message's timestamp and
        /// sender type. Used to detect chats awaiting an agent reply.
        /// </summary>
        public async Task<Dictionary<Guid, (DateTime Timestamp, string SenderType)>> GetLastMessagePerSessionAsync(List<Guid> sessionIds)
        {
            if (sessionIds.Count == 0) return new();
            var msgs = await _db.ChatMessages
                .Where(m => sessionIds.Contains(m.SessionId) && !m.IsWhisper)
                .Select(m => new { m.SessionId, m.Timestamp, m.SenderType })
                .ToListAsync();
            return msgs
                .GroupBy(m => m.SessionId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var last = g.OrderByDescending(x => x.Timestamp).First();
                        return (last.Timestamp, last.SenderType);
                    });
        }

        public async Task<List<ChatSession>> GetSessionsByAgentAsync(int agentId)
            => await _db.ChatSessions
                .Where(s => s.AgentId == agentId && s.Status == "Active")
                .OrderBy(s => s.AcceptedAt)
                .ToListAsync();

        public async Task<List<ChatMessage>> GetMessagesAsync(Guid sessionId, bool includeWhispers = false)
            => await _db.ChatMessages
                .Where(m => m.SessionId == sessionId && (includeWhispers || !m.IsWhisper))
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

        // ── Analytics (no migration — computed from existing columns) ─────────

        /// <summary>Per-day session volume vs. resolved count for the last <paramref name="days"/> days.</summary>
        public async Task<List<object>> GetVolumeSeriesAsync(int days)
        {
            if (days < 1) days = 1;
            var since = DateTime.UtcNow.Date.AddDays(-(days - 1));

            var sessions = await _db.ChatSessions
                .Where(s => s.QueuedAt >= since || (s.ResolvedAt != null && s.ResolvedAt >= since))
                .Select(s => new { s.QueuedAt, s.Status, s.ResolvedAt })
                .ToListAsync();

            var result = new List<object>();
            for (int i = days - 1; i >= 0; i--)
            {
                var day  = DateTime.UtcNow.Date.AddDays(-i);
                var next = day.AddDays(1);
                result.Add(new
                {
                    day      = day.ToString("dd MMM"),
                    sessions = sessions.Count(s => s.QueuedAt >= day && s.QueuedAt < next),
                    resolved = sessions.Count(s => s.Status == "Resolved" && s.ResolvedAt != null
                                                   && s.ResolvedAt >= day && s.ResolvedAt < next)
                });
            }
            return result;
        }

        /// <summary>Average pickup time (AcceptedAt − QueuedAt) in seconds, grouped by hour for today.</summary>
        public async Task<List<object>> GetResponseTimeByHourAsync()
        {
            var today    = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            var sessions = await _db.ChatSessions
                .Where(s => s.AcceptedAt != null && s.AcceptedAt >= today && s.AcceptedAt < tomorrow)
                .Select(s => new { s.QueuedAt, s.AcceptedAt })
                .ToListAsync();

            return Enumerable.Range(0, 24).Select(h =>
            {
                var items = sessions.Where(s => s.AcceptedAt!.Value.Hour == h).ToList();
                var avg   = items.Count == 0
                    ? 0
                    : items.Average(s => (s.AcceptedAt!.Value - s.QueuedAt).TotalSeconds);
                return (object)new { hour = $"{h:D2}", avg = (int)Math.Round(avg) };
            }).ToList();
        }

        /// <summary>
        /// Pickup / first-response / response averages (formatted HH:MM:SS) plus a per-day numeric
        /// series for the last <paramref name="days"/> days. Pass <paramref name="agentId"/> to scope
        /// to one agent, or null for workspace-wide.
        /// </summary>
        public async Task<object> GetSessionMetricsAsync(int? agentId, int days)
        {
            if (days < 1) days = 1;
            var since = DateTime.UtcNow.Date.AddDays(-(days - 1));

            var query = _db.ChatSessions.Where(s => s.AcceptedAt != null && s.AcceptedAt >= since);
            if (agentId.HasValue) query = query.Where(s => s.AgentId == agentId.Value);

            var sessions = await query
                .Select(s => new { s.Id, s.QueuedAt, s.AcceptedAt })
                .ToListAsync();

            var sessionIds = sessions.Select(s => s.Id).ToList();

            var messages = await _db.ChatMessages
                .Where(m => sessionIds.Contains(m.SessionId) && !m.IsWhisper)
                .Select(m => new { m.SessionId, m.SenderType, m.Timestamp })
                .ToListAsync();

            var msgsBySession = messages
                .GroupBy(m => m.SessionId)
                .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Timestamp).ToList());

            var perSession = new List<(DateTime day, double pickup, double? firstResponse, double? response)>();

            foreach (var s in sessions)
            {
                var pickup = (s.AcceptedAt!.Value - s.QueuedAt).TotalSeconds;
                double? firstResponse = null;
                double? response = null;

                if (msgsBySession.TryGetValue(s.Id, out var msgs))
                {
                    var firstAgent = msgs.FirstOrDefault(m => m.SenderType == "Agent" || m.SenderType == "Admin");
                    if (firstAgent != null)
                        firstResponse = (firstAgent.Timestamp - s.AcceptedAt.Value).TotalSeconds;

                    // Average gap from each customer message to the agent's next reply.
                    var gaps = new List<double>();
                    DateTime? pendingCustomer = null;
                    foreach (var m in msgs)
                    {
                        if (m.SenderType == "Customer")
                        {
                            pendingCustomer ??= m.Timestamp;
                        }
                        else if ((m.SenderType == "Agent" || m.SenderType == "Admin") && pendingCustomer != null)
                        {
                            gaps.Add((m.Timestamp - pendingCustomer.Value).TotalSeconds);
                            pendingCustomer = null;
                        }
                    }
                    if (gaps.Count > 0) response = gaps.Average();
                }

                perSession.Add((s.AcceptedAt.Value.Date, pickup, firstResponse, response));
            }

            var series = new List<object>();
            for (int i = days - 1; i >= 0; i--)
            {
                var day      = DateTime.UtcNow.Date.AddDays(-i);
                var dayItems = perSession.Where(p => p.day == day).ToList();
                series.Add(new
                {
                    date          = day.ToString("d MMM"),
                    pickup        = (int)Math.Round(dayItems.Select(p => p.pickup).DefaultIfEmpty(0).Average()),
                    response      = (int)Math.Round(dayItems.Where(p => p.response != null)
                                                            .Select(p => p.response!.Value).DefaultIfEmpty(0).Average()),
                    firstResponse = (int)Math.Round(dayItems.Where(p => p.firstResponse != null)
                                                            .Select(p => p.firstResponse!.Value).DefaultIfEmpty(0).Average())
                });
            }

            var avgPickup        = perSession.Select(p => p.pickup).DefaultIfEmpty(0).Average();
            var avgResponse      = perSession.Where(p => p.response != null).Select(p => p.response!.Value).DefaultIfEmpty(0).Average();
            var avgFirstResponse = perSession.Where(p => p.firstResponse != null).Select(p => p.firstResponse!.Value).DefaultIfEmpty(0).Average();

            return new
            {
                avgPickup        = FmtHms(avgPickup),
                avgResponse      = FmtHms(avgResponse),
                avgFirstResponse = FmtHms(avgFirstResponse),
                series
            };
        }

        private static string FmtHms(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
        }
    }
}
