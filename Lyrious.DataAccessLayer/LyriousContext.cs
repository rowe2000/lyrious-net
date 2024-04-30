using Lyrious.CoreLib.Enums;
using Lyrious.CoreLib.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Lyrious.DataAccessLayer;

public class LyriousContext : DbContext
{
    private readonly DbContextType dbContextType;

    public DbSet<Group> Groups { get; set; }
    public DbSet<GroupState> GroupStates { get; set; }
    public DbSet<Member> Members { get; set; }
    public DbSet<Membership> Memberships { get; set; }
    public DbSet<Play> Plays { get; set; }
    public DbSet<Playlog> Playlogs { get; set; }
    public DbSet<Setlist> Setlists { get; set; }
    public DbSet<SetlistItem> SetlistItems { get; set; }
    public DbSet<Song> Songs { get; set; }
    public DbSet<Songbook> Songbooks { get; set; }

    public LyriousContext() : this(DbContextType.Sqllite)
    {
    }

    public LyriousContext(DbContextType dbContextType)
    {
        this.dbContextType = dbContextType;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        //base.OnModelCreating(modelBuilder);

        var group = modelBuilder.Entity<Group>();
        group.ToTable(nameof(Groups));
        group.HasKey(o => o.Id);

        group.HasOne(p => p.GroupState);

        group.HasMany(b => b.Memberships)
            .WithOne(o => o.Group)
            .HasForeignKey(o => o.GroupId);
        group.HasMany(b => b.JoinedMembers)
            .WithOne(o => o.JoinedGroup)
            .HasForeignKey(o => o.JoinedGroupId);
        group.HasMany(o => o.Setlists)
            .WithOne(o => o.Group)
            .HasForeignKey(o => o.GroupId);
        group.HasMany(o => o.Songbooks)
            .WithOne(o => o.Group)
            .HasForeignKey(o => o.GroupId);
        group.HasMany(o => o.Playlogs)
            .WithOne(o => o.Group)
            .HasForeignKey(o => o.GroupId);

        var groupState = modelBuilder.Entity<GroupState>();
		groupState.HasKey(o => o.Id);

		groupState.ToTable(nameof(GroupStates));
        groupState.HasOne(o => o.CurrentPlay);
        groupState.HasOne(o => o.CurrentSetlist);

        var member = modelBuilder.Entity<Member>();
		member.HasKey(o => o.Id);
		
        member.ToTable(nameof(Members));
        member.HasOne(p => p.JoinedGroup);
        member.HasMany(b => b.Memberships)
            .WithOne(o => o.Member)
            .HasForeignKey(o => o.MemberId);

        var membership = modelBuilder.Entity<Membership>();
		membership.HasKey(o => o.Id);
		membership.ToTable(nameof(Memberships));
        membership
            .HasOne(o => o.Group)
            .WithMany(o => o.Memberships)
            .HasForeignKey(o => o.GroupId);
        membership
            .HasOne(o => o.Member)
            .WithMany(o => o.Memberships)
            .HasForeignKey(o => o.MemberId);

        var play = modelBuilder.Entity<Play>();
		play.HasKey(o => o.Id);

		play.ToTable(nameof(Plays));
        play.HasOne(o => o.Conductor);
        play.HasOne(o => o.Playlog)
            .WithMany(o => o.Plays)
            .HasForeignKey(o => o.PlaylogId);
        //play.HasOne(o => o.KeyString);
        play.HasOne(o => o.Song);

        var playlog = modelBuilder.Entity<Playlog>();
		playlog.HasKey(o => o.Id);

		playlog.ToTable(nameof(Playlogs));
        playlog.HasOne(o => o.Group)
            .WithMany(o => o.Playlogs)
            .HasForeignKey(o => o.GroupId);
        playlog.HasMany(o => o.Plays)
            .WithOne(o => o.Playlog)
            .HasForeignKey(o => o.PlaylogId);

        var setlist = modelBuilder.Entity<Setlist>();
		setlist.HasKey(o => o.Id);

		setlist.ToTable(nameof(Setlists));
        setlist.HasOne(p => p.Group)
            .WithMany(b => b.Setlists)
            .HasForeignKey(o => o.GroupId);
        setlist.HasMany(p => p.SetlistItems)
            .WithOne(b => b.Setlist)
            .HasForeignKey(b => b.SetlistId);

        var setlistItem = modelBuilder.Entity<SetlistItem>();
		setlistItem.HasKey(o => o.Id);
		setlistItem.ToTable(nameof(SetlistItems));
        setlistItem.HasOne(o => o.Song)
            .WithMany(o => o.SetlistItems)
            .HasForeignKey(o => o.SongId);
        setlistItem.HasOne(o => o.Setlist)
            .WithMany(o => o.SetlistItems)
            .HasForeignKey(o => o.SetlistId);

        var song = modelBuilder.Entity<Song>();
		song.HasKey(o => o.Id);

		song.ToTable(nameof(Songs));
        song.HasOne(o => o.Songbook)
            .WithMany(o => o.Songs)
            .HasForeignKey(o => o.SongbookId);
        song.HasMany(o => o.SetlistItems)
            .WithOne(o => o.Song)
            .HasForeignKey(o => o.SongId);

        var songbook = modelBuilder.Entity<Songbook>();
		songbook.HasKey(o => o.Id);
		songbook.ToTable(nameof(Songbooks));
        songbook.HasOne(o => o.Group)
            .WithMany(o => o.Songbooks)
            .HasForeignKey(o => o.GroupId);
        songbook.HasMany(o => o.Songs)
            .WithOne(o => o.Songbook)
            .HasForeignKey(o => o.SongbookId);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        switch (dbContextType)
        {
            case DbContextType.Sqllite:
                optionsBuilder.UseSqlite("Filename=data.bin");
                break;
            case DbContextType.SqlServer:
                optionsBuilder.UseSqlServer(@"Server=localhost\SQLEXPRESS;Database=Lyrious;User Id=sa;Password=1234;");
				//optionsBuilder.UseSqlServer(@"Data Source=localhost\SQLEXPRESS; Initial Catalog=Lyrious; Integrated Security=True;Pooling=False");


				break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public static void CreateTestData()
    {
        var db = new LyriousContext(DbContextType.Sqllite);
        if (!db.Database.EnsureCreated())
        {
            Console.WriteLine("Database not ensure created!");
        }

        var jonzons = Group.Create("Jonzons");
        var norraGlädjen = Group.Create("Norra Glädjen");
        var utmarken = Group.Create("Utmarken");
        var captainCrew = Group.Create("Captain Crew");

        var rw = Member.Create("Robert Westman", "", "", "+46706343840");
        var mb = Member.Create("Maria Berggren", "", "", "+46706343840");
        var tj = Member.Create("Torbjörn Jonsson", "", "", "+46706343840");
        var al = Member.Create("Andreas Löfqvist", "", "", "+46706343840");
        var tp = Member.Create("Tobias Pettersson", "", "", "+46706343840");
        var rj = Member.Create("Robert Jonsson", "", "", "+46706343840");
        var hb = Member.Create("Helena Bertholdsson", "", "", "+46706343840");
        var mp = Member.Create("Markus Porsklev", "", "", "+46706343840");
        var mg = Member.Create("Mattias Gyllengahm", "", "", "+46706343840");
        var ch = Member.Create("Chrille", "", "", "");
        var fl = Member.Create("Fredrik Lundqvist", "", "", "");

        jonzons.AddMembership(rw, Role.Admin);
        jonzons.AddMembership(mb, Role.Conductor);
        jonzons.AddMembership(tj);
        jonzons.AddMembership(al);
        jonzons.AddMembership(tp);

        norraGlädjen.AddMembership(rw, Role.Admin);
        norraGlädjen.AddMembership(rj, Role.Conductor);
        norraGlädjen.AddMembership(hb);
        norraGlädjen.AddMembership(mp);

        utmarken.AddMembership(mg, Role.Admin);
        utmarken.AddMembership(rj, Role.Conductor);
        utmarken.AddMembership(ch);

        captainCrew.AddMembership(fl, Role.Admin);
        captainCrew.AddMembership(tp);

        var song1 = Song.Create(jonzons.Songbooks[0], "The Best", "F", 120);
        var song2 = Song.Create(jonzons.Songbooks[0], "Burning Love", "G", 120);
        var song3 = Song.Create(jonzons.Songbooks[0], "Bye Bye Jonny", "G#", 130);
        var song4 = Song.Create(jonzons.Songbooks[0], "Back in black", "Em", 140);
        var song5 = Song.Create(jonzons.Songbooks[0], "Blackbird", "D#m", 150);

        var setlist = Setlist.Create("Setlist 1", jonzons, song1, song2, song3, song4, song5);
        //db.Add(setlist);
        db.Add(jonzons);
        db.SaveChanges();
    }

    public EntityEntry<Membership> AddMembership(Member member, Group group, Role role = Role.Member)
    {
        return Add(Membership.Create(member, group, role));
    }
}

public enum DbContextType
{
    NoDatabase,
    Sqllite,
    SqlServer,
}