using Microsoft.EntityFrameworkCore;
using MwohServer.Models;

namespace MwohServer.Data
{
    public class MwohDbContext : DbContext
    {
        public DbSet<UserAccount> Users => Set<UserAccount>();
        public DbSet<PlayerProfile> Profiles => Set<PlayerProfile>();
        public DbSet<CardTemplate> CardTemplates => Set<CardTemplate>();
        public DbSet<PlayerCard> PlayerCards => Set<PlayerCard>();
        public DbSet<ItemTemplate> ItemTemplates => Set<ItemTemplate>();
        public DbSet<PlayerInventoryItem> PlayerInventoryItems => Set<PlayerInventoryItem>();
        public DbSet<ShieldTeamMember> ShieldTeamMembers => Set<ShieldTeamMember>();
        public DbSet<RallyLog> RallyLogs => Set<RallyLog>();

        public MwohDbContext(DbContextOptions<MwohDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure one-to-one relationship between User and Profile
            modelBuilder.Entity<UserAccount>()
                .HasOne(u => u.Profile)
                .WithOne(p => p.UserAccount)
                .HasForeignKey<PlayerProfile>(p => p.UserAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure ShieldTeamMember bidirectional relationships
            modelBuilder.Entity<ShieldTeamMember>()
                .HasOne(m => m.Profile)
                .WithMany()
                .HasForeignKey(m => m.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ShieldTeamMember>()
                .HasOne(m => m.MemberProfile)
                .WithMany()
                .HasForeignKey(m => m.MemberProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure RallyLog relationships
            modelBuilder.Entity<RallyLog>()
                .HasOne(r => r.SenderProfile)
                .WithMany()
                .HasForeignKey(r => r.SenderProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RallyLog>()
                .HasOne(r => r.ReceiverProfile)
                .WithMany()
                .HasForeignKey(r => r.ReceiverProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure one-to-many relationship between Profile and PlayerCards
            modelBuilder.Entity<PlayerCard>()
                .HasOne(pc => pc.PlayerProfile)
                .WithMany(p => p.Cards)
                .HasForeignKey(pc => pc.PlayerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure relationship between PlayerCard and CardTemplate
            modelBuilder.Entity<PlayerCard>()
                .HasOne(pc => pc.CardTemplate)
                .WithMany()
                .HasForeignKey(pc => pc.CardTemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure relationship between PlayerProfile and PlayerInventoryItem
            modelBuilder.Entity<PlayerInventoryItem>()
                .HasOne(pi => pi.PlayerProfile)
                .WithMany(p => p.InventoryItems)
                .HasForeignKey(pi => pi.PlayerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure relationship between PlayerInventoryItem and ItemTemplate
            modelBuilder.Entity<PlayerInventoryItem>()
                .HasOne(pi => pi.ItemTemplate)
                .WithMany()
                .HasForeignKey(pi => pi.ItemTemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            // Seed default test account
            modelBuilder.Entity<UserAccount>().HasData(new UserAccount
            {
                Id = 1,
                Username = "testuser",
                PasswordHash = "password",
                CreatedAt = new System.DateTime(2026, 5, 22, 0, 0, 0, System.DateTimeKind.Utc),
                ActiveToken = "test_token_12345"
            });

            modelBuilder.Entity<PlayerProfile>().HasData(new PlayerProfile
            {
                Id = 1,
                UserAccountId = 1,
                Nickname = "TestAgent",
                Level = 50,
                Experience = 25000,
                EnergyMax = 120,
                EnergyCurrent = 120,
                AttackPower = 180,
                DefensePower = 150,
                MobaCoinBalance = 999999,
                SilverBalance = 1000000,
                PlayerIdString = "123456",
                SessionId = "test_session_67890",
                StatPoints = 15
            });
        }
    }
}
