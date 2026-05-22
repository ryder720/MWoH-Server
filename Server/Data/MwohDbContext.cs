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
                SessionId = "test_session_67890"
            });
        }
    }
}
