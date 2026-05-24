using Microsoft.AspNetCore.Identity;

namespace InotifyRelay.Data.Entities;

public class ApplicationUser : IdentityUser
{
    public bool IsExternal { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
