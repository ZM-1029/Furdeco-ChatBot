namespace Furdeco_ChatBot.Models
{
    public class TicketNote
    {
        public Guid     Id         { get; set; } = Guid.NewGuid();
        public Guid     TicketId   { get; set; }
        public string   Content    { get; set; } = null!;
        public string   AuthorName { get; set; } = null!;
        public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;

        // Navigation
        public Ticket Ticket { get; set; } = null!;
    }
}
