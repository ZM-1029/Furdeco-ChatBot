namespace Furdeco_ChatBot.Models
{
    public class Notification
    {
        public Guid   Id            { get; set; } = Guid.NewGuid();

        /// <summary>e.g. "NewChat" | "SLABreach" | "AgentOffline" | "SessionResolved" | "ChatTransferred"</summary>
        public string Type          { get; set; } = null!;
        public string Message       { get; set; } = null!;

        /// <summary>"Admin" | "Agent" | "All"</summary>
        public string TargetRole    { get; set; } = null!;
        public int?   TargetAgentId { get; set; }

        public bool   IsRead        { get; set; }
        public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    }
}
