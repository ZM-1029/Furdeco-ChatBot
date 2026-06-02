using Furdeco_ChatBot.Models;
using Microsoft.EntityFrameworkCore;

namespace Furdeco_ChatBot.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<AgentUser>    Agents        { get; set; } = null!;
        public DbSet<ChatSession>  ChatSessions  { get; set; } = null!;
        public DbSet<ChatMessage>  ChatMessages  { get; set; } = null!;
        public DbSet<Ticket>       Tickets       { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
        public DbSet<CannedReply>  CannedReplies { get; set; } = null!;
        public DbSet<TicketNote>   TicketNotes   { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder m)
        {
            // ── AgentUser ────────────────────────────────────────────
            m.Entity<AgentUser>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.Email).IsUnique();
                e.Property(x => x.Role).HasMaxLength(20);
                e.Property(x => x.Status).HasMaxLength(20);
            });

            // ── ChatSession ──────────────────────────────────────────
            m.Entity<ChatSession>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.Status, x.QueuedAt });
                e.Property(x => x.Status).HasMaxLength(20);

                e.HasOne(x => x.Agent)
                 .WithMany(a => a.AssignedSessions)
                 .HasForeignKey(x => x.AgentId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            // ── ChatMessage ──────────────────────────────────────────
            m.Entity<ChatMessage>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.SessionId, x.Timestamp });
                e.Property(x => x.SenderType).HasMaxLength(20);

                e.HasOne(x => x.Session)
                 .WithMany(s => s.Messages)
                 .HasForeignKey(x => x.SessionId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── Ticket ───────────────────────────────────────────────
            m.Entity<Ticket>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.Status, x.CreatedAt });
                e.Property(x => x.Status).HasMaxLength(20);
                e.Property(x => x.Priority).HasMaxLength(20);
                e.Property(x => x.Tags).HasColumnType("text[]");

                e.HasOne(x => x.AssignedAgent)
                 .WithMany(a => a.AssignedTickets)
                 .HasForeignKey(x => x.AssignedAgentId)
                 .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(x => x.Session)
                 .WithOne(s => s.Ticket)
                 .HasForeignKey<Ticket>(x => x.SessionId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            // ── Notification ─────────────────────────────────────────
            m.Entity<Notification>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.TargetAgentId, x.IsRead });
                e.Property(x => x.Type).HasMaxLength(50);
                e.Property(x => x.TargetRole).HasMaxLength(20);

                e.HasOne(x => x.TargetAgent)
                 .WithMany(a => a.Notifications)
                 .HasForeignKey(x => x.TargetAgentId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── RefreshToken ─────────────────────────────────────────
            m.Entity<RefreshToken>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Agent)
                 .WithMany(a => a.RefreshTokens)
                 .HasForeignKey(x => x.AgentId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── CannedReply ──────────────────────────────────────────
            m.Entity<CannedReply>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.SortOrder);
            });

            // ── TicketNote ───────────────────────────────────────────
            m.Entity<TicketNote>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.TicketId, x.CreatedAt });

                e.HasOne(x => x.Ticket)
                 .WithMany(t => t.Notes)
                 .HasForeignKey(x => x.TicketId)
                 .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
