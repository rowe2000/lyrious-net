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

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		//base.OnModelCreating(modelBuilder);


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
		optionsBuilder.EnableSensitiveDataLogging();
		optionsBuilder.EnableDetailedErrors();
		
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