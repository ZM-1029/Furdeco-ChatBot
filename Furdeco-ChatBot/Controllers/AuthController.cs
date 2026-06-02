using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Furdeco_ChatBot.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AgentUserService _users;

        public AuthController(AgentUserService users) => _users = users;

        // POST /api/auth/login
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(new { error = "Email and password required." });

            var (user, token, refreshToken) = await _users.AuthenticateAsync(req.Email, req.Password);
            if (user == null)
                return Unauthorized(new { error = "Invalid email or password." });

            return Ok(new
            {
                token,
                refreshToken,
                user = new { user.Id, user.Name, user.Email, user.Phone, user.Role, user.Status, user.AvatarUrl }
            });
        }

        // POST /api/auth/refresh
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
        {
            var result = await _users.RefreshAsync(req.RefreshToken);
            if (result == null)
                return Unauthorized(new { error = "Invalid or expired refresh token." });

            var (user, newJwt) = result.Value;
            return Ok(new
            {
                token = newJwt,
                user  = new { user!.Id, user.Name, user.Email, user.Phone, user.Role, user.Status }
            });
        }

        // GET /api/auth/me
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(idClaim, out var id))
                return Unauthorized();

            var user = await _users.GetByIdAsync(id);
            if (user == null) return NotFound();

            return Ok(new { user.Id, user.Name, user.Email, user.Phone, user.Role, user.Status, user.AvatarUrl });
        }

        // PUT /api/auth/me  — update own profile
        [HttpPut("me")]
        [Authorize]
        public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest req)
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(idClaim, out var id))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Email))
                return BadRequest(new { error = "Name and email are required." });

            var result = await _users.UpdateProfileAsync(id, req.Name, req.Email, req.Phone);
            if (result == null)
                return BadRequest(new { error = "That email is already in use." });

            var (user, token) = result.Value;
            return Ok(new
            {
                token,
                user = new { user.Id, user.Name, user.Email, user.Phone, user.Role, user.Status, user.AvatarUrl }
            });
        }

        // POST /api/auth/change-password
        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(idClaim, out var id))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
                return BadRequest(new { error = "Current and new password are required." });

            if (req.NewPassword.Length < 6)
                return BadRequest(new { error = "New password must be at least 6 characters." });

            var ok = await _users.ChangePasswordAsync(id, req.CurrentPassword, req.NewPassword);
            if (!ok)
                return BadRequest(new { error = "Current password is incorrect." });

            return Ok(new { success = true });
        }
    }

    public record LoginRequest(string Email, string Password);
    public record RefreshRequest(string RefreshToken);
    public record UpdateProfileRequest(string Name, string Email, string? Phone);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
}
