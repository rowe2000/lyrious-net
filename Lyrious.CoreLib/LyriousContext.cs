using Lyrious.CoreLib.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lyrious.CoreLib;

public class LyriousContext : LocalContext<Member, Role>
{
	public LyriousContext(Cache cache) : base(cache)
	{
	}

	public LyriousContext(DbContextOptions<LyriousContext> options, Cache cache) : base(options, cache)
	{
	}

	public DbSet<Group> Groups { get; set; }
	//public DbSet<GroupState> GroupStates { get; set; }
	public DbSet<Member> Members { get; set; }
	public DbSet<Membership> Memberships { get; set; }
	public DbSet<Play> Plays { get; set; }
	public DbSet<Playlog> Playlogs { get; set; }
	public DbSet<Setlist> Setlists { get; set; }
	public DbSet<SetlistItem> SetlistItems { get; set; }
	public DbSet<Song> Songs { get; set; }
	public DbSet<Songbook> Songbooks { get; set; }
	public DbSet<SongSetting> SongSettings { get; set; }
	public DbSet<ReleaseNote> ReleaseNotes { get; set; }

	/// <summary>
	/// Creates the database if there is none, and brings an existing one up to date: EnsureCreated
	/// doesn't add columns that were added to the model after the database was made, so they're
	/// added here (with their defaults), keeping the data.
	/// </summary>
	/// <returns>True if the database was created.</returns>
	public bool EnsureCreatedAndUpToDate()
	{
		var created = Database.EnsureCreated();
		if (!created)
		{
			var sqlite = Database.IsSqlite();
			AddColumnIfMissing(nameof(Songs), nameof(Song.MediaLink), sqlite ? "TEXT NOT NULL DEFAULT ''" : "nvarchar(500) NOT NULL DEFAULT ''");
			AddColumnIfMissing(nameof(Songs), nameof(Song.Tags), sqlite ? "TEXT NOT NULL DEFAULT ''" : $"{NVarChar(Song.MaxTagsLength)} NOT NULL DEFAULT ''");
			AddColumnIfMissing(nameof(Songs), nameof(Song.OriginalKey), sqlite ? "TEXT NOT NULL DEFAULT ''" : "nvarchar(10) NOT NULL DEFAULT ''");
			AddColumnIfMissing(nameof(Songs), nameof(Song.Artist), sqlite ? "TEXT NOT NULL DEFAULT ''" : "nvarchar(100) NOT NULL DEFAULT ''");
			AddColumnIfMissing(nameof(Members), nameof(Member.LastLoginAt), sqlite ? "TEXT NULL" : "datetime2 NULL");
			AddColumnIfMissing(nameof(Groups), nameof(Group.IsPrivate), sqlite ? "INTEGER NOT NULL DEFAULT 0" : "bit NOT NULL DEFAULT 0");
			AddColumnIfMissing(nameof(Groups), nameof(Group.MidiCommands), sqlite ? "TEXT NULL" : $"{NVarChar(Group.MaxMidiCommandsLength)} NULL");
			AddColumnIfMissing(nameof(Groups), nameof(Group.MidiExpressions), sqlite ? "TEXT NULL" : $"{NVarChar(Group.MaxMidiExpressionsLength)} NULL");
			AddColumnIfMissing(nameof(Groups), nameof(Group.LyricsHeader), sqlite ? "TEXT NULL" : $"{NVarChar(Group.MaxLyricsHeaderLength)} NULL");
			AddColumnIfMissing(nameof(Groups), nameof(Group.NextSongOnStopAtEnd), sqlite ? "INTEGER NULL" : "bit NULL");
			CreateSongSettingsIfMissing(sqlite);
			AddColumnIfMissing(nameof(SongSettings), nameof(SongSetting.TransposeOff), sqlite ? "INTEGER NOT NULL DEFAULT 0" : "bit NOT NULL DEFAULT 0");
			AddColumnIfMissing(nameof(Members), nameof(Member.Settings), sqlite ? "TEXT NULL" : $"{NVarChar(Member.MaxSettingsLength)} NULL");
			CreateReleaseNotesIfMissing(sqlite);
		}

		return created;
	}

	/// <summary>
	/// SQL Server's text type for at most that many characters: nvarchar(n) holds up to 4000, so a
	/// longer limit is nvarchar(max) (as Entity Framework makes it when it creates the database).
	/// </summary>
	private static string NVarChar(int length) => length > 4000 ? "nvarchar(max)" : $"nvarchar({length})";

	/// <summary>The members' own song settings (added after the first databases were made).</summary>
	private void CreateSongSettingsIfMissing(bool sqlite)
	{
		var sql = sqlite
			? """
			  CREATE TABLE IF NOT EXISTS "SongSettings" (
			    "Id" TEXT NOT NULL CONSTRAINT "PK_SongSettings" PRIMARY KEY,
			    "MemberId" TEXT NOT NULL, "SongId" TEXT NOT NULL,
			    "Transpose" INTEGER NOT NULL DEFAULT 0, "Lyrics" TEXT NOT NULL DEFAULT '',
			    "CreatedAt" TEXT NOT NULL, "ChangedAt" TEXT NOT NULL,
			    "Removed" INTEGER NOT NULL DEFAULT 0, "Checksum" INTEGER NOT NULL DEFAULT 0)
			  """
			: $"""
			  IF OBJECT_ID('SongSettings') IS NULL
			  BEGIN
			    CREATE TABLE [SongSettings] (
			      [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_SongSettings] PRIMARY KEY,
			      [MemberId] uniqueidentifier NOT NULL, [SongId] uniqueidentifier NOT NULL,
			      [Transpose] int NOT NULL DEFAULT 0, [Lyrics] {NVarChar(SongSetting.MaxLyricsLength)} NOT NULL DEFAULT '',
			      [CreatedAt] datetime2 NOT NULL, [ChangedAt] datetime2 NOT NULL,
			      [Removed] bit NOT NULL DEFAULT 0, [Checksum] bigint NOT NULL DEFAULT 0);
			    PRINT 'created'
			  END
			  """;
		ExecuteSql(sql);
	}

	/// <summary>The app's release notes (added after the first databases were made).</summary>
	private void CreateReleaseNotesIfMissing(bool sqlite)
	{
		ExecuteSql(sqlite
			? """
			  CREATE TABLE IF NOT EXISTS "ReleaseNotes" (
			    "Id" TEXT NOT NULL CONSTRAINT "PK_ReleaseNotes" PRIMARY KEY,
			    "Text" TEXT NOT NULL DEFAULT '', "Branch" TEXT NOT NULL DEFAULT '', "Version" TEXT NOT NULL DEFAULT '',
			    "Published" INTEGER NOT NULL DEFAULT 0, "Created" TEXT NOT NULL,
			    "CreatedAt" TEXT NOT NULL, "ChangedAt" TEXT NOT NULL,
			    "Removed" INTEGER NOT NULL DEFAULT 0, "Checksum" INTEGER NOT NULL DEFAULT 0)
			  """
			: $"""
			  IF OBJECT_ID('ReleaseNotes') IS NULL
			  BEGIN
			    CREATE TABLE [ReleaseNotes] (
			      [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_ReleaseNotes] PRIMARY KEY,
			      [Text] {NVarChar(ReleaseNote.MaxTextLength)} NOT NULL DEFAULT '', [Branch] nvarchar(100) NOT NULL DEFAULT '',
			      [Version] nvarchar(50) NOT NULL DEFAULT '', [Published] bit NOT NULL DEFAULT 0, [Created] datetime2 NOT NULL,
			      [CreatedAt] datetime2 NOT NULL, [ChangedAt] datetime2 NOT NULL,
			      [Removed] bit NOT NULL DEFAULT 0, [Checksum] bigint NOT NULL DEFAULT 0);
			  END
			  """);
	}

	/// <summary>Runs a statement on the database (opening the connection for it if it's closed).</summary>
	private void ExecuteSql(string sql)
	{
		var connection = Database.GetDbConnection();
		var wasClosed = connection.State != System.Data.ConnectionState.Open;
		if (wasClosed)
		{
			connection.Open();
		}

		try
		{
			using var command = connection.CreateCommand();
			command.CommandText = sql;
			command.ExecuteNonQuery();
		}
		finally
		{
			if (wasClosed)
			{
				connection.Close();
			}
		}
	}

	private void AddColumnIfMissing(string table, string column, string definition)
	{
		var connection = Database.GetDbConnection();
		var wasClosed = connection.State != System.Data.ConnectionState.Open;
		if (wasClosed)
		{
			connection.Open();
		}

		try
		{
			// SQLite and SQL Server differ in how to ask for a table's columns and how to add one.
			var sqlite = Database.IsSqlite();
			using (var command = connection.CreateCommand())
			{
				command.CommandText = sqlite
					? $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE lower(name) = lower('{column}')"
					: $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('{table}') AND name = '{column}'";
				if (Convert.ToInt32(command.ExecuteScalar()) > 0)
				{
					return;
				}
			}

			using (var alter = connection.CreateCommand())
			{
				alter.CommandText = sqlite
					? $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}"
					: $"ALTER TABLE [{table}] ADD [{column}] {definition}";
				alter.ExecuteNonQuery();
			}

			Console.WriteLine($"Database: added the column {table}.{column}.");
		}
		finally
		{
			if (wasClosed)
			{
				connection.Close();
			}
		}
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// Identity's own model (users, roles, claims, logins, tokens and, since .NET 10, passkeys).
		// Without it, Identity tables added in newer versions have no key configured and startup fails.
		base.OnModelCreating(modelBuilder);


		// For all entity types that derive from Entity
		foreach (var entityType in modelBuilder.Model.GetEntityTypes()
			         .Where(e => typeof(Entity).IsAssignableFrom(e.ClrType)))
		{
			var entity = modelBuilder.Entity(entityType.ClrType);
			entity
				.Property("Id")
				.ValueGeneratedNever();
		}
		
		//modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("AspNetUserClaims");
		//modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("AspNetRoleClaims");
		//modelBuilder.Entity<IdentityUserClaim<Guid>>().HasKey(c => c.Id);
		//modelBuilder.Entity<IdentityRoleClaim<Guid>>().HasKey(c => c.Id);

		//var identityRole = modelBuilder.Entity<Role>();
		//identityRole.ToTable(nameof(Role));
		//identityRole.HasKey(o => o.Id);
		//identityRole.HasIndex(e => e.Id).IsUnique();

		//var identityUser = modelBuilder.Entity<Member>();
		//identityUser.ToTable(nameof(Member));
		//identityUser.HasKey(o => o.Id);
		//identityUser.HasIndex(e => e.Id).IsUnique();
		//identityUser.HasMany(u => u.Claims).WithOne().HasForeignKey(c => c.UserId);
		//identityUser.HasMany(u => u.Logins).WithOne().HasForeignKey(l => l.UserId);
		//identityUser.HasMany(u => u.Roles).WithOne().HasForeignKey(r => r.UserId);
		//identityUser.HasMany(u => u.Tokens).WithOne().HasForeignKey(t => t.UserId);
		// Configure other relationships as needed
		var identityUserLogin = modelBuilder.Entity<IdentityUserLogin<Guid>>();
		identityUserLogin.HasKey(login => new { login.LoginProvider, login.ProviderKey });

		var identityUserRole = modelBuilder.Entity<IdentityUserRole<Guid>>();
		identityUserRole.HasKey(role => new { role.RoleId, role.UserId });

		var identityUserToken = modelBuilder.Entity<IdentityUserToken<Guid>>();
		identityUserToken.HasKey(token => new { token.UserId, token.LoginProvider, token.Name });

		var group = modelBuilder.Entity<Group>();
		group.ToTable(nameof(Groups));
		group.HasKey(o => o.Id);
		group.HasIndex(e => e.Id)
			.IsUnique();

		//group
		//	.Ignore(g => g.CurrentSetlist)
		//	.Ignore(g => g.CurrentSetlistItem)
		//	.Ignore(g => g.CurrentPlay);

		group
			.HasMany(b => b.Memberships)
			.WithOne(o => o.Group)
			.HasForeignKey(o => o.GroupId)
			.OnDelete(DeleteBehavior.Restrict);
		group
			.HasMany(b => b.JoinedMembers)
			.WithOne(o => o.JoinedGroup)
			.HasForeignKey(o => o.JoinedGroupId)
			.OnDelete(DeleteBehavior.Restrict);
		group
			.HasMany(o => o.Setlists)
			.WithOne(o => o.Group)
			.HasForeignKey(o => o.GroupId)
			.OnDelete(DeleteBehavior.Restrict);
		group
			.HasMany(o => o.Songbooks)
			.WithOne(o => o.Group)
			.HasForeignKey(o => o.GroupId)
			.OnDelete(DeleteBehavior.Restrict);
		group
			.HasMany(o => o.Playlogs)
			.WithOne(o => o.Group)
			.HasForeignKey(o => o.GroupId)
			.OnDelete(DeleteBehavior.Restrict);

		group
			.HasOne(o => o.CurrentSetlist)
			.WithOne()
			.HasForeignKey<Group>(o => o.CurrentSetlistId)
			.OnDelete(DeleteBehavior.NoAction)
			.IsRequired(false);
		group
			.HasOne(o => o.CurrentPlay)
			.WithOne()
			.HasForeignKey<Group>(o => o.CurrentPlayId)
			.OnDelete(DeleteBehavior.NoAction)
			.IsRequired(false);
		group
			.HasOne(o => o.CurrentSetlistItem)
			.WithOne()
			.HasForeignKey<Group>(o => o.CurrentSetlistItemId)
			.OnDelete(DeleteBehavior.NoAction)
			.IsRequired(false);

		var member = modelBuilder.Entity<Member>();
		member.ToTable(nameof(Members));
		member.HasKey(o => o.Id);
		member.HasIndex(e => e.Id)
			.IsUnique();

		member
			.HasOne(p => p.JoinedGroup)
			.WithMany(o => o.JoinedMembers)
			.HasForeignKey(o => o.JoinedGroupId)
			.OnDelete(DeleteBehavior.Restrict);

		member
			.HasMany(b => b.Memberships)
			.WithOne(o => o.Member)
			.HasForeignKey(o => o.MemberId);

		var membership = modelBuilder.Entity<Membership>();
		membership.ToTable(nameof(Memberships));
		membership.HasKey(o => o.Id);
		membership.HasIndex(e => e.Id)
			.IsUnique();

		membership
			.HasOne(o => o.Group)
			.WithMany(o => o.Memberships)
			.HasForeignKey(o => o.GroupId);
		membership
			.HasOne(o => o.Member)
			.WithMany(o => o.Memberships)
			.HasForeignKey(o => o.MemberId);

		var play = modelBuilder.Entity<Play>();
		play.ToTable(nameof(Plays));
		play.HasKey(o => o.Id);
		play.HasIndex(e => e.Id)
			.IsUnique();
		
		play.HasOne(o => o.Conductor)
			.WithMany()
			.HasForeignKey(o => o.ConductorId);
		play.HasOne(o => o.Playlog)
			.WithMany(o => o.Plays)
			.HasForeignKey(o => o.PlaylogId);
		play.HasOne(o => o.Song)
			.WithMany()
			.HasForeignKey(o => o.SongId);

		var playlog = modelBuilder.Entity<Playlog>();
		playlog.ToTable(nameof(Playlogs));
		playlog.HasKey(o => o.Id);
		playlog.HasIndex(e => e.Id)
			.IsUnique();
		
		playlog.HasOne(o => o.Group)
			.WithMany(o => o.Playlogs)
			.HasForeignKey(o => o.GroupId);
		playlog.HasMany(o => o.Plays)
			.WithOne(o => o.Playlog)
			.HasForeignKey(o => o.PlaylogId);

		var setlist = modelBuilder.Entity<Setlist>();
		setlist.ToTable(nameof(Setlists));
		setlist.HasKey(o => o.Id);
		setlist.HasIndex(e => e.Id)
			.IsUnique();
		
		setlist
			.HasOne(p => p.Group)
			.WithMany(b => b.Setlists)
			.HasForeignKey(o => o.GroupId)
			.OnDelete(DeleteBehavior.Restrict);
		
		setlist
			.HasMany(p => p.SetlistItems)
			.WithOne(b => b.Setlist)
			.HasForeignKey(b => b.SetlistId);

		var setlistItem = modelBuilder.Entity<SetlistItem>();
		setlistItem.ToTable(nameof(SetlistItems));
		setlistItem.HasKey(o => o.Id);
		setlistItem.HasIndex(e => e.Id)
			.IsUnique();
		
		setlistItem
			.HasOne(o => o.Song)
			.WithMany(o => o.SetlistItems)
			.HasForeignKey(o => o.SongId);
		setlistItem
			.HasOne(o => o.Setlist)
			.WithMany(o => o.SetlistItems)
			.HasForeignKey(o => o.SetlistId);

		var song = modelBuilder.Entity<Song>();
		song.HasKey(o => o.Id);
		song.ToTable(nameof(Songs));
		song.HasIndex(e => e.Id)
			.IsUnique();
		
		song.HasOne(o => o.Songbook)
			.WithMany(o => o.Songs)
			.HasForeignKey(o => o.SongbookId);
		song.HasMany(o => o.SetlistItems)
			.WithOne(o => o.Song)
			.HasForeignKey(o => o.SongId);

		var songbook = modelBuilder.Entity<Songbook>();
		songbook.HasKey(o => o.Id);
		songbook.ToTable(nameof(Songbooks));
		songbook.HasIndex(e => e.Id)
			.IsUnique();
		
		songbook
			.HasOne(o => o.Group)
			.WithMany(o => o.Songbooks)
			.HasForeignKey(o => o.GroupId);
		songbook
			.HasMany(o => o.Songs)
			.WithOne(o => o.Songbook)
			.HasForeignKey(o => o.SongbookId);
	}

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		// Sensitive data logging (values in logged queries and errors) is turned on by each app where
		// it configures the database, and only for development: on a server it'd put members' data in the logs.

		//switch (dbContextType1)
		//{
		//	case DbContextType.Sqllite:
		//		optionsBuilder.UseSqlite("Filename=data.bin");
		//		break;

		//	case DbContextType.SqlServer:
		//		optionsBuilder.UseSqlServer(@"Server=localhost\SQLEXPRESS;Database=Lyrious;User Id=sa;Password=1234;Encrypt=false;");
		//		//optionsBuilder.UseSqlServer(@"Data Source=localhost\SQLEXPRESS; Initial Catalog=Lyrious; Integrated Security=True;Pooling=False");
		//		break;

		//	case DbContextType.PostgreSql:
		//		//optionsBuilder.UseNpgsql("Host=localhost;Database=mydb;Username=myuser;Password=mypassword");
		//		break;
		//}
	}
}