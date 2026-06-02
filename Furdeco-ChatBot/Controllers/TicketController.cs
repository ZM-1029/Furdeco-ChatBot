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
                    })
            });
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
}
