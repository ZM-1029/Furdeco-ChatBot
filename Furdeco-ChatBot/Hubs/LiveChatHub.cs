using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace Furdeco_ChatBot.Hubs
{
    /// <summary>
    /// Single SignalR hub for all real-time live-chat events.
    ///
    /// Groups used:
    ///   "admins"           → all connected admin users
    ///   "agents"           → all connected agents
    ///   "agent:{agentId}"  → one specific agent (private channel)
    ///   "session:{id}"     → customer + assigned agent for one chat
    /// </summary>
    public class LiveChatHub : Hub
    {
        private readonly ChatSessionService  _sessions;
        private readonly AgentUserService    _agents;
        private readonly TicketService       _tickets;
        private readonly NotificationService _notifs;

        // ConnectionId → agentId (for online presence tracking)
        private static readonly ConcurrentDictionary<string, Guid> _agentConnections = new();

        public LiveChatHub(ChatSessionService sessions, AgentUserService agents,
            TicketService tickets, NotificationService notifs)
        {
            _sessions = sessions;
            _agents   = agents;
            _tickets  = tickets;
            _notifs   = notifs;
        }

        // ── CUSTOMER METHODS ─────────────────────────────────────────

        /// <summary>
        /// Customer joins the queue. Returns { sessionId, position }.
        /// Guard: backend trusts the OTP was verified client-side (chatbot enforces it).
        /// </summary>
        public async Task<object> JoinQueue(string reference, string postcode,
            string customerName, string issueDescription)
        {
            var session = await _sessions.CreateAndQueueAsync(reference, customerName, issueDescription);
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
                await _tickets.CreateFromSessionAsync(session);
                var agentSessions = await _sessions.GetSessionsByAgentAsync(session.AgentId.Value);
                var newStatus = agentSessions.Count == 0 ? "Online" : "Busy";
                await _agents.UpdateStatusAsync(session.AgentId.Value, newStatus);
                await Clients.Group("admins").SendAsync("AgentStatusChanged",
                    session.AgentId, newStatus);
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
                agentName    = session.Agent?.Name,
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

        // ── AGENT METHODS ────────────────────────────────────────────

        /// <summary>Agent connects and sets themselves Online.</summary>
        public async Task AgentConnect(Guid agentId)
        {
            _agentConnections[Context.ConnectionId] = agentId;
            await Groups.AddToGroupAsync(Context.ConnectionId, "agents");
            await Groups.AddToGroupAsync(Context.ConnectionId, $"agent:{agentId}");
            await _agents.UpdateStatusAsync(agentId, "Online");
            await Clients.Group("admins").SendAsync("AgentStatusChanged", agentId, "Online");

            // Send current queue snapshot to newly connected agent
            var snapshot = await BuildQueueSnapshot();
            await Clients.Caller.SendAsync("QueueUpdated", snapshot);
        }

        /// <summary>Agent changes their own status.</summary>
        public async Task UpdateStatus(Guid agentId, string status)
        {
            await _agents.UpdateStatusAsync(agentId, status);
            await Clients.Group("admins").SendAsync("AgentStatusChanged", agentId, status);
            await Clients.Group("agents").SendAsync("AgentStatusChanged", agentId, status);
        }

        /// <summary>
        /// Agent accepts the next chat from the queue.
        /// Uses FOR UPDATE SKIP LOCKED to prevent race conditions.
        /// </summary>
        public async Task<object?> AcceptNextChat(Guid agentId)
        {
            var session = await _sessions.DequeueNextAsync();
            if (session == null)
            {
                await Clients.Caller.SendAsync("QueueEmpty");
                return null;
            }

            var agent = await _agents.GetByIdAsync(agentId);
            if (agent == null) return null;

            await _sessions.AssignToAgentAsync(session.Id, agentId, agent.Name);
            await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{session.Id}");

            // Notify customer
            await Clients.Group($"session:{session.Id}")
                .SendAsync("AgentJoined", agent.Name);

            // Update admin view
            await Clients.Group("admins").SendAsync("SessionAssigned", session.Id, agentId);

            // Update queue for everyone
            var snapshot = await BuildQueueSnapshot();
            await Clients.Group("agents").SendAsync("QueueUpdated", snapshot);
            await Clients.Group("admins").SendAsync("QueueUpdated", snapshot);

            // Mark agent Busy
            await _agents.UpdateStatusAsync(agentId, "Busy");
            await Clients.Group("admins").SendAsync("AgentStatusChanged", agentId, "Busy");

            return new { session.Id, session.Reference, session.CustomerName, session.IssueDescription };
        }

        /// <summary>Agent sends a message to the customer.</summary>
        public async Task AgentSendMessage(Guid sessionId, Guid agentId, string content)
        {
            var agent = await _agents.GetByIdAsync(agentId);
            if (agent == null) return;

            var msg = await _sessions.AddMessageAsync(sessionId, "Agent", agent.Name, content);

            await Clients.Group($"session:{sessionId}")
                .SendAsync("MessageReceived", new
                {
                    msg.Id, msg.SessionId, msg.SenderType, msg.SenderName,
                    msg.Content, msg.IsWhisper,
                    timestamp = msg.Timestamp
                });
        }

        /// <summary>Agent typing indicator.</summary>
        public async Task AgentTyping(Guid sessionId)
            => await Clients.Group($"session:{sessionId}").SendAsync("AgentTyping");

        public async Task AgentStoppedTyping(Guid sessionId)
            => await Clients.Group($"session:{sessionId}").SendAsync("AgentStoppedTyping");

        /// <summary>Agent resolves the chat — auto-creates a ticket.</summary>
        public async Task ResolveChat(Guid sessionId, Guid agentId, string? notes)
        {
            var session = await _sessions.ResolveAsync(sessionId, notes);
            if (session == null) return;

            await Clients.Group($"session:{sessionId}").SendAsync("ChatEnded");
            await _tickets.CreateFromSessionAsync(session);

            var agentSessions = await _sessions.GetSessionsByAgentAsync(agentId);
            var newStatus = agentSessions.Count == 0 ? "Online" : "Busy";
            await _agents.UpdateStatusAsync(agentId, newStatus);
            await Clients.Group("admins").SendAsync("AgentStatusChanged", agentId, newStatus);
            await Clients.Group("admins").SendAsync("SessionResolved", sessionId);
        }

        /// <summary>Agent transfers the chat to another agent.</summary>
        public async Task TransferChat(Guid sessionId, Guid newAgentId)
        {
            var newAgent = await _agents.GetByIdAsync(newAgentId);
            if (newAgent == null) return;

            await _sessions.TransferAsync(sessionId, newAgentId, newAgent.Name);

            await Clients.Group($"session:{sessionId}")
                .SendAsync("MessageReceived", new
                {
                    SenderType = "System",
                    SenderName = "System",
                    Content    = $"Chat transferred to {newAgent.Name}",
                    IsWhisper  = false,
                    Timestamp  = DateTime.UtcNow
                });

            await Clients.Group($"agent:{newAgentId}").SendAsync("ChatTransferred", new
            {
                SessionId    = sessionId,
                AgentName    = newAgent.Name
            });

            await _notifs.CreateAsync("ChatTransferred",
                $"Chat transferred to you from another agent", "Agent", newAgentId);
        }

        // ── ADMIN METHODS ────────────────────────────────────────────

        /// <summary>Admin joins admin room — receives all events and state dump.</summary>
        public async Task JoinAdminRoom()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "admins");

            var activeSessions = await _sessions.GetActiveSessionsAsync();
            var queue          = await _sessions.GetQueueAsync();
            var allAgents      = await _agents.GetAllAsync();

            await Clients.Caller.SendAsync("AdminStateDump", new
            {
                activeSessions = activeSessions.Select(MapSession),
                queue          = queue.Select(MapSession),
                agents         = allAgents.Select(MapAgent)
            });
        }

        /// <summary>Admin sends a private whisper to an agent (not visible to customer).</summary>
        public async Task WhisperToAgent(Guid sessionId, Guid agentId, string content)
        {
            await _sessions.AddMessageAsync(sessionId, "Admin", "Supervisor", content, isWhisper: true);

            await Clients.Group($"agent:{agentId}").SendAsync("WhisperReceived", new
            {
                SessionId = sessionId,
                Content   = content,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>Admin barges into a session — their messages go to the customer.</summary>
        public async Task BargeIn(Guid sessionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");

            var session = await _sessions.GetSessionAsync(sessionId);
            if (session != null)
            {
                session.IsSupervised = true;
                // Persist the supervised flag (direct EF update via session service)
            }

            await Clients.Group($"session:{sessionId}")
                .SendAsync("SupervisorJoined", "A supervisor has joined to assist.");
        }

        // ── DISCONNECT ───────────────────────────────────────────────

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (_agentConnections.TryRemove(Context.ConnectionId, out var agentId))
            {
                await _agents.UpdateStatusAsync(agentId, "Offline");
                await Clients.Group("admins").SendAsync("AgentStatusChanged", agentId, "Offline");

                // Notify admin if agent had active chats
                var activeSessions = await _sessions.GetSessionsByAgentAsync(agentId);
                if (activeSessions.Count > 0)
                    await _notifs.CreateAsync("AgentOffline",
                        $"An agent went offline with {activeSessions.Count} active chat(s)", "Admin");
            }
            await base.OnDisconnectedAsync(exception);
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

        private static object MapSession(Models.ChatSession s) => new
        {
            s.Id, s.Reference, s.CustomerName, s.IssueDescription,
            s.Status, s.QueuedAt, s.AcceptedAt,
            agentName = s.Agent?.Name,
            agentId   = s.AgentId
        };

        private static object MapAgent(Models.AgentUser a) => new
        {
            a.Id, a.Name, a.Email, a.Role, a.Status,
            a.AvatarUrl, a.LastSeenAt
        };
    }
}
