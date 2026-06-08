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
    }
}
