using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Furdeco_ChatBot.Controllers
{
    [Route("api/analytics")]
    [ApiController]
    //[Authorize]
    public class AnalyticsController : ControllerBase
    {
        private readonly ChatSessionService _sessions;

        public AnalyticsController(ChatSessionService sessions)
        {
            _sessions = sessions;
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
            int? agentId = null;
            if (scope == "all")
            {
                if (User.FindFirstValue(ClaimTypes.Role) != "Admin") return Forbid();
            }
            else
            {
                if (!int.TryParse(User.FindFirstValue("UserId"), out var id)) return Unauthorized();
                agentId = id;
            }
            return Ok(await _sessions.GetSessionMetricsAsync(agentId, days));
        }
    }
}
