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

        /// <summary>When true, SUNDAY uses its own window below instead of the
        /// common one above (Mon–Sat are unaffected). Off = every day the same.</summary>
        public bool LiveChatSundayEnabled { get; set; }
        /// <summary>"HH:mm" UK time — Sunday only.</summary>
        public string LiveChatSundayStartTime { get; set; } = "07:00";
        public string LiveChatSundayEndTime { get; set; } = "18:00";
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

        /// <summary>
        /// The window that applies RIGHT NOW on the UK clock: Sunday uses its own
        /// hours when the Sunday override is on; Mon–Sat (and Sunday without the
        /// override) use the common window.
        /// </summary>
        public static (string start, string end) EffectiveWindow(WorkspaceSetting s)
        {
            var nowUk = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Uk());
            return s.LiveChatSundayEnabled && nowUk.DayOfWeek == DayOfWeek.Sunday
                ? (s.LiveChatSundayStartTime, s.LiveChatSundayEndTime)
                : (s.LiveChatStartTime, s.LiveChatEndTime);
        }

        public static bool IsOpenNow(WorkspaceSetting? s)
        {
            if (s == null || !s.LiveChatHoursEnabled) return true;
            var (startStr, endStr) = EffectiveWindow(s);
            if (!TimeSpan.TryParse(startStr, out var start) ||
                !TimeSpan.TryParse(endStr, out var end) || start == end) return true;
            var nowUk = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Uk()).TimeOfDay;
            return start < end
                ? nowUk >= start && nowUk < end
                : nowUk >= start || nowUk < end;   // window spans midnight
        }
    }
}
