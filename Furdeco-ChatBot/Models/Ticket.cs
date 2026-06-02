namespace Furdeco_ChatBot.Models
{
    public class Ticket
    {
        public Guid   Id              { get; set; } = Guid.NewGuid();
        public Guid?  SessionId       { get; set; }   // source chat session

        public string Subject         { get; set; } = null!;

        /// <summary>"Open" | "InProgress" | "Escalated" | "Resolved"</summary>
        public string Status          { get; set; } = "Open";

        /// <summary>"Low" | "Medium" | "High" | "Urgent"</summary>
        public string Priority        { get; set; } = "Medium";

        public Guid?  AssignedAgentId { get; set; }
        public string CustomerName    { get; set; } = null!;
        public string Reference       { get; set; } = null!;

        public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt     { get; set; } = DateTime.UtcNow;
        public DateTime SlaDeadline   { get; set; }

        /// <summary>PostgreSQL text array column.</summary>
        public string[] Tags          { get; set; } = Array.Empty<string>();

        // Navigation
        public AgentUser?   AssignedAgent { get; set; }
        public ChatSession? Session       { get; set; }
    }
}
