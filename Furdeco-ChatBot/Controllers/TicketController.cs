using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Furdeco_ChatBot.Controllers
{
    [Route("api/tickets")]
    [ApiController]
    [Authorize]
    public class TicketController : ControllerBase
    {
        private readonly TicketService _tickets;

        public TicketController(TicketService tickets) => _tickets = tickets;

        // GET /api/tickets?status=Open&priority=High&search=ref
        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] string? status,
            [FromQuery] string? priority,
            [FromQuery] Guid?   agentId,
            [FromQuery] string? search)
        {
            var tickets = await _tickets.GetAllAsync(status, priority, agentId, search);
            return Ok(tickets.Select(t => new
            {
                t.Id, t.Subject, t.Status, t.Priority,
                t.CustomerName, t.Reference,
                t.CreatedAt, t.UpdatedAt, t.SlaDeadline,
                t.Tags,
                // Customer's 1-5 star rating of the source chat (null if not rated)
                customerRating = t.Session?.CustomerRating,
                // Agent-selected chat category at resolve time (null if not set)
                chatType = t.Session?.ChatType,
                assignedAgent = t.AssignedAgent == null ? null : new
                {
                    t.AssignedAgent.Id, t.AssignedAgent.Name
                },
                // SLA breach flag — useful for UI badge
                slaBreach = t.SlaDeadline < DateTime.UtcNow && t.Status != "Resolved"
            }));
        }

        // GET /api/tickets/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var ticket = await _tickets.GetByIdAsync(id);
            if (ticket == null) return NotFound();

            return Ok(new
            {
                ticket.Id, ticket.Subject, ticket.Status, ticket.Priority,
                ticket.CustomerName, ticket.Reference,
                ticket.CreatedAt, ticket.UpdatedAt, ticket.SlaDeadline,
                ticket.Tags,
                customerRating = ticket.Session?.CustomerRating,
                chatType = ticket.Session?.ChatType,
                slaBreach = ticket.SlaDeadline < DateTime.UtcNow && ticket.Status != "Resolved",
                assignedAgent = ticket.AssignedAgent == null ? null : new
                {
                    ticket.AssignedAgent.Id, ticket.AssignedAgent.Name
                },
                messages = ticket.Session?.Messages
                    .Where(m => !m.IsWhisper)
                    .OrderBy(m => m.Timestamp)
                    .Select(m => new
                    {
                        m.SenderType, m.SenderName, m.Content, m.Timestamp
                    }),
                notes = ticket.Notes
                    .OrderBy(n => n.CreatedAt)
                    .Select(n => new
                    {
                        n.Id, n.Content, n.AuthorName, n.CreatedAt
                    })
            });
        }

        // POST /api/tickets/{id}/notes
        [HttpPost("{id:guid}/notes")]
        public async Task<IActionResult> AddNote(Guid id, [FromBody] AddNoteRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Content))
                return BadRequest(new { error = "Note content is required." });

            var authorName = User.Identity?.Name ?? "Agent";
            var note = await _tickets.AddNoteAsync(id, req.Content.Trim(), authorName);
            return Ok(new { note.Id, note.Content, note.AuthorName, note.CreatedAt });
        }

        // PUT /api/tickets/{id}
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTicketRequest req)
        {
            var ticket = await _tickets.UpdateAsync(id, req.Status, req.Priority,
                req.AssignedAgentId, req.Tags);
            if (ticket == null) return NotFound();
            return Ok(new { ticket.Id, ticket.Status, ticket.Priority, ticket.UpdatedAt });
        }
    }

    public record UpdateTicketRequest(
        string?  Status,
        string?  Priority,
        Guid?    AssignedAgentId,
        string[]? Tags);

    public record AddNoteRequest(string Content);
}
