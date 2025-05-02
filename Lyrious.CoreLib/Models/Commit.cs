
using System.Runtime.Serialization;

namespace Lyrious.CoreLib.Models;

public class Commit : Entity
{
    [DataMember] public Guid? ParentCommitId { get; set; }
    [DataMember] public Guid ObjectId { get; set; }

    [DataMember] public string Type { get; set; } = "";
    [DataMember] public string Value { get; set; } = "";

    [DataMember] public Commit? ParentCommit { get; set; } = null;
    [DataMember] public Guid ChangedBy { get; set; } = Guid.Empty;
}