namespace Furdeco_ChatBot.Models
{
    public class AgentUser
    {
        public Guid   Id           { get; set; } = Guid.NewGuid();
        public string Name         { get; set; } = null!;
        public string Email        { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;

        /// <summary>"Admin" or "Agent"</summary>
        public string Role         { get; set; } = "Agent";

        /// <summary>"Online" | "Busy" | "Away" | "Offline"</summary>
        public string Status       { get; set; } = "Offline";

        public string? AvatarUrl   { get; set; }
        public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;
        public DateTime? LastSeenAt { get; set; }

        // Navigation
        public ICollection<ChatSession>  AssignedSessions { get; set; } = new List<ChatSession>();
        public ICollection<Ticket>       AssignedTickets  { get; set; } = new List<Ticket>();
        public ICollection<Notification> Notifications    { get; set; } = new List<Notification>();
        public ICollection<RefreshToken> RefreshTokens    { get; set; } = new List<RefreshToken>();
    }
}
