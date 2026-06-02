namespace Furdeco_ChatBot.Models
{
    /// <summary>
    /// A reusable reply snippet shared across the whole support team and
    /// surfaced in the live-chat composer.
    /// </summary>
    public class CannedReply
    {
        public Guid     Id        { get; set; } = Guid.NewGuid();
        public string   Text      { get; set; } = null!;
        public int      SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
