using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Furdeco_ChatBot.Controllers
{
    [Route("api/livechat")]
    [ApiController]
    public class LiveChatController : ControllerBase
    {
        private readonly ChatSessionService _sessions;

        public LiveChatController(ChatSessionService sessions) => _sessions = sessions;

        // GET /api/livechat/sessions/{id}  — used by chatbot session restore check
        [HttpGet("sessions/{id:guid}")]
        public async Task<IActionResult> GetSession(Guid id)
        {
            var session = await _sessions.GetSessionAsync(id);
            if (session == null) return NotFound();

            return Ok(new
            {
                session.Id,
                session.Status,
                session.Reference,
                session.CustomerName,
                agentName     = session.Agent?.Name,
                agentId       = session.AgentId,
                queuePosition = session.Status == "Queued"
                    ? await _sessions.GetQueuePositionAsync(id)
                    : (int?)null,
                messages = session.Messages
                    .Where(m => !m.IsWhisper)
                    .OrderBy(m => m.Timestamp)
                    .Select(m => new
                    {
                        m.SenderType, m.SenderName, m.Content,
                        timestamp = m.Timestamp
                    })
            });
        }

        // GET /api/livechat/queue  (Admin / Agent)
        [HttpGet("queue")]
        [Authorize]
        public async Task<IActionResult> GetQueue()
        {
            var queue = await _sessions.GetQueueAsync();
            return Ok(queue.Select((s, i) => new
            {
                s.Id, s.Reference, s.CustomerName, s.IssueDescription,
                s.QueuedAt, position = i + 1
            }));
        }

        // GET /api/livechat/sessions/active  (Admin)
        [HttpGet("sessions/active")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetActive()
        {
            var sessions = await _sessions.GetActiveSessionsAsync();
            return Ok(sessions.Select(s => new
            {
                s.Id, s.Reference, s.CustomerName, s.Status,
                s.QueuedAt, s.AcceptedAt,
                agentName = s.Agent?.Name,
                agentId   = s.AgentId
            }));
        }

        // GET /api/livechat/sessions/agent/{agentId}  (Agent)
        [HttpGet("sessions/agent/{agentId:guid}")]
        [Authorize]
        public async Task<IActionResult> GetAgentSessions(Guid agentId)
        {
            var sessions = await _sessions.GetSessionsByAgentAsync(agentId);
            return Ok(sessions.Select(s => new
            {
                s.Id, s.Reference, s.CustomerName, s.IssueDescription,
                s.Status, s.QueuedAt, s.AcceptedAt,
                agentName = s.Agent?.Name,
                agentId   = s.AgentId
            }));
        }

        // POST /api/livechat/sessions/{id}/rate  — called by chatbot after chat ends
        [HttpPost("sessions/{id:guid}/rate")]
        public async Task<IActionResult> Rate(Guid id, [FromBody] RateRequest req)
        {
            if (req.Rating < 1 || req.Rating > 5)
                return BadRequest(new { error = "Rating must be 1–5." });
            await _sessions.RateAsync(id, req.Rating);
            return Ok();
        }
    }

    public record RateRequest(int Rating);
}
