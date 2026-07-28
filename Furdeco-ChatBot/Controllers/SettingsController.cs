using Furdeco_ChatBot.Models;
using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Furdeco_ChatBot.Controllers
{
    [Route("api/settings")]
    [ApiController]
   // [Authorize]
    public class SettingsController : ControllerBase
    {
        private readonly SettingsService _settings;

        public SettingsController(SettingsService settings) => _settings = settings;

        // GET /api/settings  — any authenticated user can read.
        [HttpGet]
        public async Task<IActionResult> Get() => Ok(Map(await _settings.GetAsync()));

        // PUT /api/settings  — admin only.
        [HttpPut]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update([FromBody] UpdateSettingsRequest req)
        {
            var s = await _settings.UpdateAsync(
                req.AutoAssignEnabled, req.MaxConcurrentChats,
                req.ResponseTimeoutSeconds, req.MaxAssignAttempts);
            return Ok(Map(s));
        }

        private static object Map(WorkspaceSetting s) => new
        {
            s.AutoAssignEnabled,
            s.MaxConcurrentChats,
            s.ResponseTimeoutSeconds,
            s.MaxAssignAttempts
        };
    }

    public record UpdateSettingsRequest(
        bool? AutoAssignEnabled,
        int?  MaxConcurrentChats,
        int?  ResponseTimeoutSeconds,
        int?  MaxAssignAttempts);
}
