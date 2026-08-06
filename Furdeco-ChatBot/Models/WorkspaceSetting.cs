namespace Furdeco_ChatBot.Models
{
    /// <summary>
    /// Single-row workspace configuration (auto-assign routing). Id is pinned to 1.
    /// </summary>
    public class WorkspaceSetting
    {
        public int  Id { get; set; } = 1;

        /// <summary>When true, queued chats are pushed to agents (round-robin) instead of pull-only.</summary>
        public bool AutoAssignEnabled { get; set; } = false;

        /// <summary>Max simultaneous chats an agent can be auto-assigned.</summary>
        public int  MaxConcurrentChats { get; set; } = 5;

        /// <summary>Seconds an agent has to send a first reply before the chat is reassigned.</summary>
        public int  ResponseTimeoutSeconds { get; set; } = 30;

        /// <summary>How many agents to try before escalating the chat to an admin.</summary>
        public int  MaxAssignAttempts { get; set; } = 3;

        /// <summary>Live-chat availability window (UK wall clock). OFF by default —
        /// when disabled customers can start a chat at any time (unchanged
        /// behaviour). Managed from the WALMS Settings page (shared DB row).</summary>
        public bool LiveChatHoursEnabled { get; set; }
        /// <summary>"HH:mm" UK time, e.g. "08:00".</summary>
        public string LiveChatStartTime { get; set; } = "08:00";
        /// <summary>"HH:mm" UK time, e.g. "20:00". End &lt; start = spans midnight.</summary>
        public string LiveChatEndTime { get; set; } = "20:00";
    }

    /// <summary>
    /// Availability evaluated on the UK wall clock (GMT/BST handled by the tz
    /// database). FAIL-OPEN: disabled setting or malformed times mean "open" —
    /// availability config must never take live chat down by accident.
    /// </summary>
    public static class LiveChatHours
    {
        private static TimeZoneInfo Uk()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"); }  // Windows id
            catch { return TimeZoneInfo.FindSystemTimeZoneById("Europe/London"); }    // Linux id
        }

        public static bool IsOpenNow(WorkspaceSetting? s)
        {
            if (s == null || !s.LiveChatHoursEnabled) return true;
            if (!TimeSpan.TryParse(s.LiveChatStartTime, out var start) ||
                !TimeSpan.TryParse(s.LiveChatEndTime, out var end) || start == end) return true;
            var nowUk = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Uk()).TimeOfDay;
            return start < end
                ? nowUk >= start && nowUk < end
                : nowUk >= start || nowUk < end;   // window spans midnight
        }
    }
}
