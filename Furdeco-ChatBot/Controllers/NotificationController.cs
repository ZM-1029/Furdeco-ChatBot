using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Furdeco_ChatBot.Controllers
{
    [Route("api/notifications")]
    [ApiController]
    //[Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly NotificationService _notifs;

        public NotificationController(NotificationService notifs) => _notifs = notifs;

        // GET /api/notifications
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var (id, role) = GetCaller();
            if (id == 0) return Unauthorized();

            var notifs = await _notifs.GetForUserAsync(id, role);
            return Ok(notifs.Select(n => new
            {
                n.Id, n.Type, n.Message, n.IsRead, n.CreatedAt
            }));
        }

        // PUT /api/notifications/{id}/read
        [HttpPut("{id:guid}/read")]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            await _notifs.MarkReadAsync(id);
            return Ok();
        }

        // PUT /api/notifications/read-all
        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllRead()
        {
            var (id, _) = GetCaller();
            if (id == 0) return Unauthorized();
            await _notifs.MarkAllReadAsync(id);
            return Ok();
        }

        private (int id, string role) GetCaller()
        {
            var idStr = User.FindFirstValue("UserId");
            var role  = User.FindFirstValue(ClaimTypes.Role) ?? "Agent";
            return int.TryParse(idStr, out var id) ? (id, role) : (0, role);
        }
    }
}
