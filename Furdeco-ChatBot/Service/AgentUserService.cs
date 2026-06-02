using Furdeco_ChatBot.Data;
using Furdeco_ChatBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Furdeco_ChatBot.Service
{
    public class AgentUserService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public AgentUserService(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        // ── Auth ─────────────────────────────────────────────────────

        public async Task<(AgentUser? user, string? token, string? refreshToken)> AuthenticateAsync(string email, string password)
        {
            var user = await _db.Agents.FirstOrDefaultAsync(a => a.Email == email.ToLower().Trim());
            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                return (null, null, null);

            user.LastSeenAt = DateTime.UtcNow;
            user.Status = "Online";

            var rt = new RefreshToken
            {
                AgentId   = user.Id,
                Token     = Guid.NewGuid().ToString(),
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            _db.RefreshTokens.Add(rt);
            await _db.SaveChangesAsync();

            return (user, GenerateJwt(user), rt.Token);
        }

        public async Task<AgentUser?> GetByIdAsync(Guid id)
            => await _db.Agents.FindAsync(id);

        public async Task<List<AgentUser>> GetAllAsync()
            => await _db.Agents.OrderBy(a => a.Name).ToListAsync();

        public async Task<AgentUser> CreateAsync(string name, string email, string password, string role = "Agent")
        {
            var user = new AgentUser
            {
                Name         = name.Trim(),
                Email        = email.ToLower().Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role         = role
            };
            _db.Agents.Add(user);
            await _db.SaveChangesAsync();
            return user;
        }

        public async Task UpdateStatusAsync(Guid id, string status)
        {
            var user = await _db.Agents.FindAsync(id);
            if (user == null) return;
            user.Status     = status;
            user.LastSeenAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        // ── Profile / password (self-service) ────────────────────────

        /// <summary>
        /// Updates the caller's own profile. Returns (user, newJwt) on success,
        /// or null with an error message if the email is already taken.
        /// A fresh JWT is issued so the updated name/email claims stay in sync.
        /// </summary>
        public async Task<(AgentUser user, string newJwt)?> UpdateProfileAsync(
            Guid id, string name, string email, string? phone)
        {
            var user = await _db.Agents.FindAsync(id);
            if (user == null) return null;

            var normalizedEmail = email.ToLower().Trim();
            var taken = await _db.Agents
                .AnyAsync(a => a.Id != id && a.Email == normalizedEmail);
            if (taken) return null;

            user.Name  = name.Trim();
            user.Email = normalizedEmail;
            user.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
            await _db.SaveChangesAsync();

            return (user, GenerateJwt(user));
        }

        /// <summary>
        /// Changes the caller's password after verifying the current one.
        /// Returns true on success, false if the current password is wrong
        /// or the user does not exist.
        /// </summary>
        public async Task<bool> ChangePasswordAsync(
            Guid id, string currentPassword, string newPassword)
        {
            var user = await _db.Agents.FindAsync(id);
            if (user == null) return false;
            if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
                return false;

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task DeleteAsync(Guid id)
        {
            var user = await _db.Agents.FindAsync(id);
            if (user == null) return;
            _db.Agents.Remove(user);
            await _db.SaveChangesAsync();
        }

        // ── JWT ──────────────────────────────────────────────────────

        public string GenerateJwt(AgentUser user)
        {
            var key  = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer:   _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name,           user.Name),
                    new Claim(ClaimTypes.Email,          user.Email),
                    new Claim(ClaimTypes.Role,           user.Role)
                },
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async Task<(AgentUser? user, string? newJwt)?> RefreshAsync(string refreshToken)
        {
            var rt = await _db.RefreshTokens
                .Include(r => r.Agent)
                .FirstOrDefaultAsync(r => r.Token == refreshToken && !r.IsRevoked && r.ExpiresAt > DateTime.UtcNow);
            if (rt == null) return null;

            rt.IsRevoked = true;
            var newRt = new RefreshToken
            {
                AgentId   = rt.AgentId,
                Token     = Guid.NewGuid().ToString(),
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            _db.RefreshTokens.Add(newRt);
            await _db.SaveChangesAsync();

            return (rt.Agent, GenerateJwt(rt.Agent));
        }
    }
}
