using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.SignalR;

namespace Furdeco_ChatBot.Hubs
{
    /// <summary>
    /// Single SignalR hub for customer-facing real-time live-chat events.
    ///
    /// The agent/admin side (login, accepting chats, presence, assignment,
    /// transfers, supervision) is now handled by WALMS — those methods have
    /// been removed from this hub.
    ///
    /// Groups used:
    ///   "admins"           → admin listeners (WALMS-side notifications)
    ///   "agents"           → agent listeners (WALMS-side notifications)
    ///   "session:{id}"     → customer + assigned agent for one chat
    /// </summary>
    public class LiveChatHub : Hub
    {
        private readonly ChatSessionService  _sessions;
        private readonly TicketService       _tickets;
        private readonly NotificationService _notifs;

        public LiveChatHub(ChatSessionService sessions,
            TicketService tickets, NotificationService notifs)
        {
            _sessions = sessions;
            _tickets  = tickets;
            _notifs   = notifs;
        }

        // ── CUSTOMER METHODS ─────────────────────────────────────────

        /// <summary>
        /// Customer joins the queue. Returns { sessionId, position }.
        /// Guard: backend trusts the OTP was verified client-side (chatbot enforces it).
        /// </summary>
        public async Task<object> JoinQueue(string reference, string postcode,
            string customerName, string issueDescription, string? orderSnapshot = null)
        {
            var session = await _sessions.CreateAndQueueAsync(reference, customerName, issueDescription, orderSnapshot);
            var position = await _sessions.GetQueuePositionAsync(session.Id);

            await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{session.Id}");

            var snapshot = await BuildQueueSnapshot();
            await Clients.Group("agents").SendAsync("QueueUpdated", snapshot);
            await Clients.Group("admins").SendAsync("QueueUpdated", snapshot);

            await _notifs.CreateAsync("NewChat",
                $"New chat from {customerName} re {reference}", "Agent");

            return new { sessionId = session.Id, position };
        }

        /// <summary>Customer sends a message to the assigned agent.</summary>
        public async Task SendMessage(Guid sessionId, string content)
        {
            var session = await _sessions.GetSessionAsync(sessionId);
            if (session == null) return;

            var msg = await _sessions.AddMessageAsync(sessionId, "Customer",
                session.CustomerName, content);

            await Clients.Group($"session:{sessionId}")
                .SendAsync("MessageReceived", new
                {
                    msg.Id, msg.SessionId, msg.SenderType, msg.SenderName,
                    msg.Content, msg.IsWhisper,
                    timestamp = msg.Timestamp
                });
        }

        /// <summary>Customer typing indicator → shown to the agent/admin.</summary>
        public async Task CustomerTyping(Guid sessionId)
            => await Clients.OthersInGroup($"session:{sessionId}").SendAsync("CustomerTyping");

        public async Task CustomerStoppedTyping(Guid sessionId)
            => await Clients.OthersInGroup($"session:{sessionId}").SendAsync("CustomerStoppedTyping");

        /// <summary>Customer leaves the queue before being picked up.</summary>
        public async Task LeaveQueue(Guid sessionId)
        {
            await _sessions.AbandonAsync(sessionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session:{sessionId}");

            var snapshot = await BuildQueueSnapshot();
            await Clients.Group("agents").SendAsync("QueueUpdated", snapshot);
            await Clients.Group("admins").SendAsync("QueueUpdated", snapshot);
        }

        /// <summary>Customer ends an active chat.</summary>
        public async Task EndChat(Guid sessionId)
        {
            var session = await _sessions.ResolveAsync(sessionId, null);
            if (session == null) return;

            await Clients.Group($"session:{sessionId}").SendAsync("ChatEnded");

            if (session.AgentId.HasValue)
            {
                var ticket = await _tickets.CreateFromSessionAsync(session);
                await Clients.Group("admins").SendAsync("TicketCreated", new { ticket.Id });
            }

            await Clients.Group("admins").SendAsync("SessionResolved", sessionId);
        }

        /// <summary>Customer reconnects after a page refresh.</summary>
        public async Task<object?> RejoinSession(Guid sessionId)
        {
            var session = await _sessions.GetSessionAsync(sessionId);
            if (session == null || (session.Status != "Active" && session.Status != "Queued"))
                return null;

            await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");

            return new
            {
                session.Status,
                agentName    = session.AgentName,
                queuePosition = session.Status == "Queued"
                    ? await _sessions.GetQueuePositionAsync(sessionId)
                    : (int?)null,
                messages = session.Messages
                    .Where(m => !m.IsWhisper)
                    .Select(m => new
                    {
                        m.SenderType, m.SenderName, m.Content,
                        timestamp = m.Timestamp
                    })
            };
        }

        // ── HELPERS ──────────────────────────────────────────────────

        private async Task<IEnumerable<object>> BuildQueueSnapshot()
        {
            var queue = await _sessions.GetQueueAsync();
            return queue.Select((s, i) => new
            {
                s.Id, s.Reference, s.CustomerName, s.IssueDescription,
                s.QueuedAt, position = i + 1
            });
        }
    }
}
