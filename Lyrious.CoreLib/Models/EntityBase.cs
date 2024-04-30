using Lyrious.CoreLib.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Lyrious.CoreLib.Models;

public abstract class EntityBase 
{
    [Key]
    [Member] public Guid Id { get; set; } = Guid.NewGuid();
    [Member] public DateTime CreatedAt { get; set; } = DateTime.Now;
    [Member] public DateTime ChangedAt { get; set; } = DateTime.Now;
    [Member] public bool Removed { get; set; } = false; 
    public long Checksum { get; set; } = 0;


    public bool VerifyChecksum() => Checksum == CalculateChecksum();

    public virtual int CalculateChecksum()
    {
            var hashCode = new HashCode();
        var propertyInfos = this.GetType().GetProperties();
        foreach (var propertyInfo in propertyInfos)
        {
            var name = propertyInfo.Name;
            var value = propertyInfo.GetValue(this, null);

            hashCode.Add(value);
        }

        return hashCode.ToHashCode();
    }

    public void UpdateChecksum()
    {
        var calculatedChecksum = CalculateChecksum();
        if (Checksum == calculatedChecksum)
            return;

        ChangedAt = DateTime.Now;
        Checksum = CalculateChecksum();
    }

    public override bool Equals(object? obj)
    {
        if (obj is null)
            return false;

        if (ReferenceEquals(this, obj))
            return true;
        
        if (obj.GetType() != GetType())
            return false;

        return obj is EntityBase entity && entity.Id == Id;
    }

    public override int GetHashCode() => Id.GetHashCode();
}