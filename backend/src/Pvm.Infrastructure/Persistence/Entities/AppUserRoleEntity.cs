namespace Pvm.Infrastructure.Persistence.Entities;

public sealed class AppUserRoleEntity
{
    public Guid Id { get; set; }
    public Guid AppUserId { get; set; }
    public required string Role { get; set; }
    public Guid? GrantedByAppUserId { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}
