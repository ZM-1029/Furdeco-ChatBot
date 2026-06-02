using Furdeco_ChatBot.Data;
using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Furdeco_ChatBot.Controllers
{
    [Route("api/analytics")]
    [ApiController]
    [Authorize]
    public class AnalyticsController : ControllerBase
    {
        private readonly ChatSessionService _sessions;
        private readonly AppDbContext _db;

        public AnalyticsController(ChatSessionService sessions, AppDbContext db)
        {
            _sessions = sessions;
            _db       = db;
        }

        // GET /api/analytics/volume?days=7  (Admin)
        [HttpGet("volume")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Volume([FromQuery] int days = 7)
            => Ok(await _sessions.GetVolumeSeriesAsync(days));

        // GET /api/analytics/response-by-hour  (Admin)
        [HttpGet("response-by-hour")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ResponseByHour()
            => Ok(await _sessions.GetResponseTimeByHourAsync());

        // GET /api/analytics/session-metrics?days=30&scope=me|all
        // scope=me  → calling agent's own sessions (default)
        // scope=all → workspace-wide (Admin only)
        [HttpGet("session-metrics")]
        public async Task<IActionResult> SessionMetrics([FromQuery] int days = 30, [FromQuery] string scope = "me")
        {
            Guid? agentId = null;
            if (scope == "all")
            {
                if (User.FindFirstValue(ClaimTypes.Role) != "Admin") return Forbid();
            }
            else
            {
                var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(idStr, out var id)) return Unauthorized();
                agentId = id;
            }
            return Ok(await _sessions.GetSessionMetricsAsync(agentId, days));
        }

        // GET /api/analytics/agents  (Admin only)
        // Per-agent performance: sessions handled, resolution rate, avg CSAT, avg first reply, tickets assigned.
        [HttpGet("agents")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AgentStats()
        {
            var agents = await _db.Agents
                .Where(a => a.Role != "Admin")
                .OrderBy(a => a.Name)
                .ToListAsync();

            var agentIds = agents.Select(a => a.Id).ToList();

            var sessions = await _db.ChatSessions
                .Where(s => s.AgentId != null && agentIds.Contains(s.AgentId!.Value))
                .Select(s => new { s.AgentId, s.Status, s.CustomerRating, s.QueuedAt, s.AcceptedAt })
                .ToListAsync();

            var tickets = await _db.Tickets
                .Where(t => t.AssignedAgentId != null && agentIds.Contains(t.AssignedAgentId!.Value))
                .Select(t => new { t.AssignedAgentId, t.Status })
                .ToListAsync();

            var result = agents.Select(a =>
            {
                var s          = sessions.Where(x => x.AgentId == a.Id).ToList();
                var resolved   = s.Count(x => x.Status == "Resolved");
                var rated      = s.Where(x => x.CustomerRating.HasValue).ToList();
                var avgCsat    = rated.Any() ? Math.Round(rated.Average(x => x.CustomerRating!.Value) * 20) : 0;
                var accepted   = s.Where(x => x.AcceptedAt.HasValue).ToList();
                var avgReplyS  = accepted.Any()
                    ? (int)Math.Round(accepted.Average(x => (x.AcceptedAt!.Value - x.QueuedAt).TotalSeconds))
                    : 0;

                return new
                {
                    a.Id,
                    a.Name,
                    a.Status,
                    sessionsHandled   = s.Count,
                    sessionsResolved  = resolved,
                    resolutionRate    = s.Count > 0 ? (int)Math.Round((double)resolved / s.Count * 100) : 0,
                    avgCsat           = (int)avgCsat,
                    avgFirstReplySecs = avgReplyS,
                    ticketsAssigned   = tickets.Count(t => t.AssignedAgentId == a.Id),
                };
            });

            return Ok(result);
        }
    }
}
