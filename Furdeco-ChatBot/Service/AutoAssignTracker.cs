using System.Collections.Concurrent;

namespace Furdeco_ChatBot.Service
{
    /// <summary>
    /// In-memory state for the auto-assign engine, shared between the SignalR hub
    /// (which marks a chat "replied") and the background assignment loop. Single
    /// instance / single server — state is transient by design.
    /// </summary>
    public class AutoAssignTracker
    {
        public class Offer
        {
            public Guid     AgentId   { get; set; }
            public DateTime OfferedAt { get; set; }
            public bool     Replied   { get; set; }
        }

        // sessionId -> current pending offer
        private readonly ConcurrentDictionary<Guid, Offer> _offers = new();
        // sessionId -> set of agent ids already tried (so we don't re-offer to them)
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, byte>> _tried = new();
        // sessionId -> escalated (gave up, manual pickup)
        private readonly ConcurrentDictionary<Guid, byte> _escalated = new();
        // agentId -> last time we assigned them a chat (for longest-idle ordering)
        private readonly ConcurrentDictionary<Guid, DateTime> _lastAssigned = new();

        public Offer? GetOffer(Guid sessionId) => _offers.TryGetValue(sessionId, out var o) ? o : null;

        public void CreateOffer(Guid sessionId, Guid agentId, DateTime now)
        {
            _offers[sessionId] = new Offer { AgentId = agentId, OfferedAt = now };
            _lastAssigned[agentId] = now;
            _tried.GetOrAdd(sessionId, _ => new ConcurrentDictionary<Guid, byte>())[agentId] = 1;
        }

        public void ClearOffer(Guid sessionId) => _offers.TryRemove(sessionId, out _);

        /// <summary>Called when an agent sends a message — counts as "responded".</summary>
        public void MarkReplied(Guid sessionId)
        {
            if (_offers.TryGetValue(sessionId, out var o)) o.Replied = true;
        }

        /// <summary>Agents that currently hold an unanswered offer (used to cap one pending offer per agent).</summary>
        public HashSet<Guid> PendingOfferAgentIds()
        {
            var set = new HashSet<Guid>();
            foreach (var o in _offers.Values)
                if (!o.Replied) set.Add(o.AgentId);
            return set;
        }

        public bool HasTried(Guid sessionId, Guid agentId)
            => _tried.TryGetValue(sessionId, out var set) && set.ContainsKey(agentId);

        public int TriedCount(Guid sessionId)
            => _tried.TryGetValue(sessionId, out var set) ? set.Count : 0;

        public void Escalate(Guid sessionId) => _escalated[sessionId] = 1;
        public bool IsEscalated(Guid sessionId) => _escalated.ContainsKey(sessionId);

        public DateTime LastAssigned(Guid agentId)
            => _lastAssigned.TryGetValue(agentId, out var t) ? t : DateTime.MinValue;

        /// <summary>Drop all per-session state once a chat leaves the queue/active set.</summary>
        public void Forget(Guid sessionId)
        {
            _offers.TryRemove(sessionId, out _);
            _tried.TryRemove(sessionId, out _);
            _escalated.TryRemove(sessionId, out _);
        }

        public IReadOnlyCollection<Guid> TrackedSessions()
            => _offers.Keys.Concat(_tried.Keys).Concat(_escalated.Keys).Distinct().ToList();
    }
}
