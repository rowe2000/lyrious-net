using Lyrious.CoreLib.Attributes;

namespace Lyrious.CoreLib.Models;

public class Commit : EntityBase
{
    [Member] public Guid? ParentCommitId { get; set; }
    [Member] public Guid ObjectId { get; set; }

    [Member] public string Type { get; set; } = "";
    [Member] public string Value { get; set; } = "";

    [Member] public Commit? ParentCommit { get; set; } = null;
    [Member] public Guid ChangedBy { get; set; } = Guid.Empty;
}