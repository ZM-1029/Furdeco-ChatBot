using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Furdeco_ChatBot.Controllers
{
    [Route("api/agents")]
    [ApiController]
   [Authorize]
    public class AgentController : ControllerBase
    {
        private readonly AgentUserService    _users;
        private readonly ChatSessionService  _sessions;

        public AgentController(AgentUserService users, ChatSessionService sessions)
        {
            _users    = users;
            _sessions = sessions;
        }

        // GET /api/agents
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var agents = await _users.GetAllAsync();
            return Ok(agents.Select(a => new
            {
                a.Id, a.Name, a.Email, a.Role, a.Status, a.AvatarUrl, a.LastSeenAt
            }));
        }

        // GET /api/agents/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var agent = await _users.GetByIdAsync(id);
            if (agent == null) return NotFound();

            var activeSessions = await _sessions.GetSessionsByAgentAsync(id);
            return Ok(new
            {
                agent.Id, agent.Name, agent.Email, agent.Role,
                agent.Status, agent.AvatarUrl, agent.LastSeenAt,
                activeChats = activeSessions.Count
            });
        }

        // PUT /api/agents/{id}/status
        [HttpPut("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] StatusRequest req)
        {
            var allowed = new[] { "Online", "Busy", "Away", "Offline" };
            if (!allowed.Contains(req.Status))
                return BadRequest(new { error = "Invalid status." });

            await _users.UpdateStatusAsync(id, req.Status);
            return Ok(new { status = req.Status });
        }

        // POST /api/agents  (Admin only)
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateAgentRequest req)
        {
            var agent = await _users.CreateAsync(req.Name, req.Email, req.Password, req.Role);
            return Ok(new { agent.Id, agent.Name, agent.Email, agent.Role });
        }

        // DELETE /api/agents/{id}  (Admin only)
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _users.DeleteAsync(id);
            return NoContent();
        }
    }

    public record StatusRequest(string Status);
    public record CreateAgentRequest(string Name, string Email, string Password, string Role = "Agent");
}
