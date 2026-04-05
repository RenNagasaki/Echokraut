using Microsoft.EntityFrameworkCore;

namespace Echokraut.DataClasses.Database;

public class EchokrautDbContext : DbContext
{
    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();
    public DbSet<CharacterContextEntity> CharacterContexts => Set<CharacterContextEntity>();
    public DbSet<CharacterInstanceEntity> CharacterInstances => Set<CharacterInstanceEntity>();
    public DbSet<VoiceClipEntity> VoiceClips => Set<VoiceClipEntity>();
    public DbSet<VoiceEntity> Voices => Set<VoiceEntity>();
    public DbSet<VoiceAllowedGenderEntity> VoiceAllowedGenders => Set<VoiceAllowedGenderEntity>();
    public DbSet<VoiceAllowedRaceEntity> VoiceAllowedRaces => Set<VoiceAllowedRaceEntity>();
    public DbSet<PhoneticCorrectionEntity> PhoneticCorrections => Set<PhoneticCorrectionEntity>();

    private readonly string _dbPath = "";

    public EchokrautDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>
    /// Constructor for EF Core tooling and testing with pre-configured options.
    /// </summary>
    public EchokrautDbContext(DbContextOptions<EchokrautDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Character: unique on (name, gender, race)
        modelBuilder.Entity<CharacterEntity>()
            .HasIndex(c => new { c.Name, c.Gender, c.Race })
            .IsUnique();

        // CharacterContext: unique on (character_id, context_type)
        modelBuilder.Entity<CharacterContextEntity>()
            .HasIndex(cc => new { cc.CharacterId, cc.ContextType })
            .IsUnique();

        // CharacterInstance: composite PK defined via attribute
        modelBuilder.Entity<CharacterInstanceEntity>()
            .HasIndex(ci => ci.NpcBaseId);

        // DialogEncounter: indexes
        modelBuilder.Entity<VoiceClipEntity>()
            .HasIndex(e => e.CharacterId);
        modelBuilder.Entity<VoiceClipEntity>()
            .HasIndex(e => e.Timestamp);
        modelBuilder.Entity<VoiceClipEntity>()
            .HasIndex(e => e.TextSource);

        // Voice: unique on backend_voice
        modelBuilder.Entity<VoiceEntity>()
            .HasIndex(v => v.BackendVoice)
            .IsUnique();

        // PhoneticCorrection: unique on original_text
        modelBuilder.Entity<PhoneticCorrectionEntity>()
            .HasIndex(p => p.OriginalText)
            .IsUnique();

        // Cascade deletes
        modelBuilder.Entity<CharacterEntity>()
            .HasMany(c => c.Contexts)
            .WithOne(cc => cc.Character)
            .HasForeignKey(cc => cc.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CharacterEntity>()
            .HasMany(c => c.Instances)
            .WithOne(ci => ci.Character)
            .HasForeignKey(ci => ci.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CharacterEntity>()
            .HasMany(c => c.Encounters)
            .WithOne(e => e.Character)
            .HasForeignKey(e => e.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<VoiceEntity>()
            .HasMany(v => v.AllowedGenders)
            .WithOne(g => g.Voice)
            .HasForeignKey(g => g.VoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<VoiceEntity>()
            .HasMany(v => v.AllowedRaces)
            .WithOne(r => r.Voice)
            .HasForeignKey(r => r.VoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
