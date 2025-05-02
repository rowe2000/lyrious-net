namespace Lyrious.CoreLib.Models;

public static class ChecksumUtil
{
	public static bool VerifyChecksum(IEntity entity)
	{
		return entity.Checksum == CalculateChecksum(entity);
	}

	public static int CalculateChecksum(IEntity entity)
	{
		var hashCode = new HashCode();
		var propertyInfos = entity.GetType().GetProperties();
		foreach (var propertyInfo in propertyInfos)
		{
			var name = propertyInfo.Name;
			var value = propertyInfo.GetValue(entity, null);

			hashCode.Add(value);
		}

		return hashCode.ToHashCode();
	}

	public static void UpdateChecksum(IEntity entity)
	{
		var calculatedChecksum = CalculateChecksum(entity);
		if (entity.Checksum == calculatedChecksum)
		{
			return;
		}

		entity.ChangedAt = DateTime.Now;
		entity.Checksum = CalculateChecksum(entity);
	}
}