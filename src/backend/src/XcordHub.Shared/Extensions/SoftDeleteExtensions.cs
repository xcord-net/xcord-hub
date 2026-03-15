namespace XcordHub.Shared.Extensions;

public static class SoftDeleteExtensions
{
    public static void SoftDelete(this ISoftDeletable entity)
    {
        entity.DeletedAt = DateTimeOffset.UtcNow;
    }
}
