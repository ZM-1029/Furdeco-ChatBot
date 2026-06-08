using Furdeco_ChatBot.Data;
using Furdeco_ChatBot.Models;
using Microsoft.EntityFrameworkCore;

namespace Furdeco_ChatBot.Service
{
    public class SettingsService
    {
        private readonly AppDbContext _db;

        public SettingsService(AppDbContext db) => _db = db;

        /// <summary>Returns the single settings row, creating it with defaults if missing.</summary>
        public async Task<WorkspaceSetting> GetAsync()
        {
            var s = await _db.WorkspaceSettings.FirstOrDefaultAsync();
            if (s == null)
            {
                s = new WorkspaceSetting();
                _db.WorkspaceSettings.Add(s);
                await _db.SaveChangesAsync();
            }
            return s;
        }

        public async Task<WorkspaceSetting> UpdateAsync(
            bool? autoAssignEnabled, int? maxConcurrentChats,
            int? responseTimeoutSeconds, int? maxAssignAttempts)
        {
            var s = await GetAsync();
            if (autoAssignEnabled.HasValue)      s.AutoAssignEnabled = autoAssignEnabled.Value;
            if (maxConcurrentChats.HasValue)     s.MaxConcurrentChats = Math.Clamp(maxConcurrentChats.Value, 1, 20);
            if (responseTimeoutSeconds.HasValue) s.ResponseTimeoutSeconds = Math.Clamp(responseTimeoutSeconds.Value, 10, 300);
            if (maxAssignAttempts.HasValue)      s.MaxAssignAttempts = Math.Clamp(maxAssignAttempts.Value, 1, 10);
            await _db.SaveChangesAsync();
            return s;
        }
    }
}
