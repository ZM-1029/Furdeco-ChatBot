using Furdeco_ChatBot.Data;
using Furdeco_ChatBot.Hubs;
using Furdeco_ChatBot.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Furdeco_ChatBot.Service
{
    public class NotificationService
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<LiveChatHub> _hub;

        public NotificationService(AppDbContext db, IHubContext<LiveChatHub> hub)
        {
            _db  = db;
            _hub = hub;
        }

        public async Task CreateAsync(string type, string message,
            string targetRole, int? targetAgentId = null)
        {
            var notif = new Notification
            {
                Type          = type,
                Message       = message,
                TargetRole    = targetRole,
                TargetAgentId = targetAgentId,
                IsRead        = false,
                CreatedAt     = DateTime.UtcNow
            };
            _db.Notifications.Add(notif);
            await _db.SaveChangesAsync();

            // Push via SignalR
            var payload = new { notif.Id, notif.Type, notif.Message, notif.CreatedAt };
            if (targetAgentId.HasValue)
                await _hub.Clients.Group($"agent:{targetAgentId}").SendAsync("NotificationReceived", payload);
            else if (targetRole == "Admin")
                await _hub.Clients.Group("admins").SendAsync("NotificationReceived", payload);
            else
                await _hub.Clients.All.SendAsync("NotificationReceived", payload);
        }

        public async Task<List<Notification>> GetForUserAsync(int agentId, string role)
        {
            var q = _db.Notifications.AsQueryable();
            if (role == "Admin")
                q = q.Where(n => n.TargetAgentId == agentId ||
                                  n.TargetRole == "Admin" ||
                                  n.TargetRole == "All");
            else
                q = q.Where(n => n.TargetAgentId == agentId ||
                                  n.TargetRole == "Agent" ||
                                  n.TargetRole == "All");

            return await q.OrderByDescending(n => n.CreatedAt).Take(50).ToListAsync();
        }

        public async Task MarkReadAsync(Guid id)
        {
            var n = await _db.Notifications.FindAsync(id);
            if (n == null) return;
            n.IsRead = true;
            await _db.SaveChangesAsync();
        }

        public async Task MarkAllReadAsync(int agentId)
        {
            // ExecuteUpdateAsync requires EF Core 7+; use load-and-save for EF Core 6 compat
            var unread = await _db.Notifications
                .Where(n => n.TargetAgentId == agentId && !n.IsRead)
                .ToListAsync();
            foreach (var n in unread) n.IsRead = true;
            if (unread.Count > 0) await _db.SaveChangesAsync();
        }
    }
}
