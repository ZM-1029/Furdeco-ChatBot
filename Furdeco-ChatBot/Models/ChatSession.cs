namespace Furdeco_ChatBot.Models
{
    public class ChatSession
    {
        public Guid   Id           { get; set; } = Guid.NewGuid();
        public string Reference    { get; set; } = null!;   // order / consignment ref
        public string CustomerName { get; set; } = null!;
        public string IssueDescription { get; set; } = string.Empty;

        /// <summary>Delivery postcode as TYPED by the customer at chat start —
        /// persisted even when the order lookup failed, so admins can reach out
        /// to customers who abandoned the queue (WALMS Chat Requests screen).</summary>
        public string? Postcode    { get; set; }

        /// <summary>"Queued" | "Active" | "Resolved" | "Abandoned" | "Transferred"</summary>
        public string Status       { get; set; } = "Queued";

        public int?   AgentId      { get; set; }
        public string? AgentName   { get; set; }
        public DateTime QueuedAt   { get; set; } = DateTime.UtcNow;
        public DateTime? AcceptedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }

        /// <summary>Last queue heartbeat from the widget while WAITING — the
        /// WALMS abandonment sweep uses this to tell a live waiter from a closed
        /// tab (2-min grace). Explicit "Leave Queue" abandons directly.</summary>
        public DateTime? LastSeenAt { get; set; }
        public string? AgentNotes  { get; set; }
        public int?   CustomerRating { get; set; }   // 1-5 stars
        public bool   IsSupervised { get; set; }     // admin barged in

        /// <summary>JSON snapshot of the looked-up order (consignment, address,
        /// postcode, delivery date/slot, status, contacts) captured at chat start
        /// so agents/admins can see order context. Null if no order was looked up.</summary>
        public string? OrderSnapshot { get; set; }

        /// <summary>Chat category selected by the agent at resolve time
        /// (e.g. "Order status", "Return", "Damage", or a manual value). Null if not set.</summary>
        public string? ChatType { get; set; }

        // Navigation
        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
        public Ticket?                  Ticket   { get; set; }
    }
}
