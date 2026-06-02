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

        public async Task<ChatSession> CreateAndQueueAsync(string reference, string customerName, string issueDescription)
        {
            var session = new ChatSession
            {
                Reference        = reference,
                CustomerName     = customerName,
                IssueDescription = issueDescription,
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

        // ── Session lifecycle ────────────────────────────────────────

        public async Task<ChatSession?> AssignToAgentAsync(Guid sessionId, Guid agentId, string agentName)
        {
            var session = await _db.ChatSessions.FindAsync(sessionId);
            if (session == null) return null;

            session.AgentId     = agentId;
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

        public async Task<ChatSession?> ResolveAsync(Guid sessionId, string? agentNotes)
        {
            var session = await _db.ChatSessions.FindAsync(sessionId);
            if (session == null) return null;

            session.Status     = "Resolved";
            session.ResolvedAt = DateTime.UtcNow;
            session.AgentNotes = agentNotes;

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

        public async Task<ChatSession?> TransferAsync(Guid sessionId, Guid newAgentId, string newAgentName)
        {
            var session = await _db.ChatSessions.FindAsync(sessionId);
            if (session == null) return null;

            session.AgentId = newAgentId;
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
                .Include(s => s.Agent)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

        public async Task<List<ChatSession>> GetActiveSessionsAsync()
            => await _db.ChatSessions
                .Include(s => s.Agent)
                .Where(s => s.Status == "Active")
                .OrderBy(s => s.AcceptedAt)
                .ToListAsync();

        public async Task<List<ChatSession>> GetSessionsByAgentAsync(Guid agentId)
            => await _db.ChatSessions
                .Where(s => s.AgentId == agentId && s.Status == "Active")
                .OrderBy(s => s.AcceptedAt)
                .ToListAsync();

        public async Task<List<ChatMessage>> GetMessagesAsync(Guid sessionId, bool includeWhispers = false)
            => await _db.ChatMessages
                .Where(m => m.SessionId == sessionId && (includeWhispers || !m.IsWhisper))
                .OrderBy(m => m.Timestamp)
                .ToListAsync();
    }
}
