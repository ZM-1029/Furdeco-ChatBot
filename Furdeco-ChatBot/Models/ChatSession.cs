namespace Furdeco_ChatBot.Models
{
    public class ChatSession
    {
        public Guid   Id           { get; set; } = Guid.NewGuid();
        public string Reference    { get; set; } = null!;   // order / consignment ref
        public string CustomerName { get; set; } = null!;
        public string IssueDescription { get; set; } = string.Empty;

        /// <summary>"Queued" | "Active" | "Resolved" | "Abandoned" | "Transferred"</summary>
        public string Status       { get; set; } = "Queued";

        public Guid?  AgentId      { get; set; }
        public DateTime QueuedAt   { get; set; } = DateTime.UtcNow;
        public DateTime? AcceptedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? AgentNotes  { get; set; }
        public int?   CustomerRating { get; set; }   // 1-5 stars
        public bool   IsSupervised { get; set; }     // admin barged in

        /// <summary>JSON snapshot of the looked-up order (consignment, address,
        /// postcode, delivery date/slot, status, contacts) captured at chat start
        /// so agents/admins can see order context. Null if no order was looked up.</summary>
        public string? OrderSnapshot { get; set; }

        // Navigation
        public AgentUser?               Agent    { get; set; }
        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
        public Ticket?                  Ticket   { get; set; }
    }
}
