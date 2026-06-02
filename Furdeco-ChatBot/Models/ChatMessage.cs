namespace Furdeco_ChatBot.Models
{
    public class ChatMessage
    {
        public Guid   Id         { get; set; } = Guid.NewGuid();
        public Guid   SessionId  { get; set; }

        /// <summary>"Customer" | "Agent" | "Admin" | "System"</summary>
        public string SenderType { get; set; } = null!;
        public string SenderName { get; set; } = null!;
        public string Content    { get; set; } = null!;

        /// <summary>Admin whisper — visible to agent only, NOT to customer.</summary>
        public bool   IsWhisper  { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Navigation
        public ChatSession Session { get; set; } = null!;
    }
}
