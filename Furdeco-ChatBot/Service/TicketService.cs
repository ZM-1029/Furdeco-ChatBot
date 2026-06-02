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
                Status          = "Open",
                Priority        = "Medium",
                AssignedAgentId = session.AgentId,
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
            Guid? agentId = null, string? search = null)
        {
            var q = _db.Tickets.Include(t => t.AssignedAgent).AsQueryable();

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
                .Include(t => t.AssignedAgent)
                .Include(t => t.Session)
                    .ThenInclude(s => s != null ? s.Messages.OrderBy(m => m.Timestamp) : null!)
                .FirstOrDefaultAsync(t => t.Id == id);

        // ── Update ───────────────────────────────────────────────────

        public async Task<Ticket?> UpdateAsync(Guid id, string? status, string? priority,
            Guid? assignedAgentId, string[]? tags)
        {
            var ticket = await _db.Tickets.FindAsync(id);
            if (ticket == null) return null;

            if (status          != null) ticket.Status          = status;
            if (priority        != null) ticket.Priority        = priority;
            if (assignedAgentId != null) ticket.AssignedAgentId = assignedAgentId;
            if (tags            != null) ticket.Tags            = tags;
            ticket.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return ticket;
        }
    }
}
