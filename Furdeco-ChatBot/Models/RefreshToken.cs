namespace Furdeco_ChatBot.Models
{
    public class RefreshToken
    {
        public Guid   Id        { get; set; } = Guid.NewGuid();
        public Guid   AgentId   { get; set; }
        public string Token     { get; set; } = null!;   // stored as plain GUID — hashing optional
        public DateTime ExpiresAt { get; set; }
        public bool   IsRevoked { get; set; }

        // Navigation
        public AgentUser Agent  { get; set; } = null!;
    }
}
