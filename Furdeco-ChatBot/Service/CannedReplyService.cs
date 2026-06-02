using Furdeco_ChatBot.Data;
using Furdeco_ChatBot.Models;
using Microsoft.EntityFrameworkCore;

namespace Furdeco_ChatBot.Service
{
    public class CannedReplyService
    {
        private readonly AppDbContext _db;

        public CannedReplyService(AppDbContext db) => _db = db;

        public async Task<List<CannedReply>> GetAllAsync() =>
            await _db.CannedReplies
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.CreatedAt)
                .ToListAsync();

        public async Task<CannedReply> CreateAsync(string text)
        {
            var maxOrder = await _db.CannedReplies.AnyAsync()
                ? await _db.CannedReplies.MaxAsync(c => c.SortOrder)
                : -1;

            var reply = new CannedReply
            {
                Text      = text.Trim(),
                SortOrder = maxOrder + 1,
                CreatedAt = DateTime.UtcNow
            };
            _db.CannedReplies.Add(reply);
            await _db.SaveChangesAsync();
            return reply;
        }

        public async Task<CannedReply?> UpdateAsync(Guid id, string text)
        {
            var reply = await _db.CannedReplies.FindAsync(id);
            if (reply == null) return null;
            reply.Text = text.Trim();
            await _db.SaveChangesAsync();
            return reply;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var reply = await _db.CannedReplies.FindAsync(id);
            if (reply == null) return false;
            _db.CannedReplies.Remove(reply);
            await _db.SaveChangesAsync();
            return true;
        }
    }
}
