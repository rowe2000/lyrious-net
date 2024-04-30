using Lyrious.CoreLib.Attributes;

namespace Lyrious.CoreLib.Models;

public class Member : EntityBase, IName
{
    [Member] public virtual string Name { get; set; } = "";
    [Member] public string Password { get; set; } = "";
    [Member] public string Email { get; set; } = "";
    [Member] public string Phone { get; set; } = "";

    [Member] public Guid? JoinedGroupId { get; set; }

    public Group? JoinedGroup { get; set; }

    public ObservableList<Membership> Memberships { get; set; } = [];

    public static Member Create(string name, string password, string email, string phone)
    {
        var member = new Member
        {
            Name = name,
            Password = password,
            Email = email,
            Phone = phone
        };

        return member;
    }
}