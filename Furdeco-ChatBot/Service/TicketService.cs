using Furdeco_ChatBot.Data;
using Furdeco_ChatBot.Models;
using Microsoft.EntityFrameworkCore;

namespace Furdeco_ChatBot.Service
{
    public class TicketService
    {
        private readonly AppDbContext _db;

        public TicketService(AppDbContext db) => _db = db;

        // ── Create ───────────────────────────────────────────────────

        public async Task<Ticket> CreateFromSessionAsync(ChatSession session)
        {
            var ticket = new Ticket
            {
                SessionId       = session.Id,
                Subject         = $"Chat re {session.Reference} — {session.CustomerName}",
                // Born RESOLVED, matching WALMS's ticket factory: this only runs when
                // the customer ends the chat, so the conversation is already finished
                // — the ticket is the record of it. Born "Open" it sat unworked and
                // flagged a phantom SLA breach 4h later. Admins can still reopen it.
                Status          = "Resolved",
                // Who ended the chat — this factory only fires on customer-side end.
                Tags            = new[] { "Closed by customer" },
                Priority        = "Medium",
                AssignedAgentId = session.AgentId,
                AssignedAgentName = session.AgentName,
                CustomerName    = session.CustomerName,
                Reference       = session.Reference,
                CreatedAt       = DateTime.UtcNow,
                UpdatedAt       = DateTime.UtcNow,
                SlaDeadline     = DateTime.UtcNow.AddHours(4)  // 4-hour SLA window
            };
            _db.Tickets.Add(ticket);
            await _db.SaveChangesAsync();
            return ticket;
        }

        // ── Queries ──────────────────────────────────────────────────

        public async Task<List<Ticket>> GetAllAsync(string? status = null, string? priority = null,
            int? agentId = null, string? search = null)
        {
            var q = _db.Tickets
                .Include(t => t.Session)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))   q = q.Where(t => t.Status   == status);
            if (!string.IsNullOrEmpty(priority)) q = q.Where(t => t.Priority == priority);
            if (agentId.HasValue)                q = q.Where(t => t.AssignedAgentId == agentId);
            if (!string.IsNullOrEmpty(search))
                q = q.Where(t => t.Subject.Contains(search) ||
                                 t.CustomerName.Contains(search) ||
                                 t.Reference.Contains(search));

            return await q.OrderByDescending(t => t.CreatedAt).ToListAsync();
        }

        public async Task<Ticket?> GetByIdAsync(Guid id)
            => await _db.Tickets
                .Include(t => t.Session)
                    .ThenInclude(s => s.Messages)
                .Include(t => t.Notes)
                .FirstOrDefaultAsync(t => t.Id == id);

        public async Task<TicketNote> AddNoteAsync(Guid ticketId, string content, string authorName)
        {
            var note = new TicketNote
            {
                TicketId   = ticketId,
                Content    = content,
                AuthorName = authorName,
                CreatedAt  = DateTime.UtcNow
            };
            _db.TicketNotes.Add(note);
            await _db.SaveChangesAsync();
            return note;
        }

        // ── Update ───────────────────────────────────────────────────

        public async Task<Ticket?> UpdateAsync(Guid id, string? status, string? priority,
            int? assignedAgentId, string? assignedAgentName, string[]? tags)
        {
            var ticket = await _db.Tickets.FindAsync(id);
            if (ticket == null) return null;

            if (status          != null) ticket.Status          = status;
            if (priority        != null) ticket.Priority        = priority;
            if (assignedAgentId != null)
            {
                ticket.AssignedAgentId   = assignedAgentId;
                ticket.AssignedAgentName = assignedAgentName;
            }
            if (tags            != null) ticket.Tags            = tags;
            ticket.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return ticket;
        }
    }
}
