namespace InotifyRelay.Data.Entities;

/// <summary>Singleton settings row (Id == 1).</summary>
public class AuthSettingsEntity
{
    public int Id { get; set; } = 1;
    public bool OidcEnabled { get; set; }
    public string? OidcAuthority { get; set; }
    public string? OidcClientId { get; set; }
    public string? OidcClientSecret { get; set; }
    public string OidcScopes { get; set; } = "openid profile email";
    public string? OidcAdminRoleClaim { get; set; }
    public string? OidcAdminRoleValue { get; set; }
    public bool AllowLocalLogin { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Singleton settings row (Id == 1).</summary>
public class SystemSettingsEntity
{
    public int Id { get; set; } = 1;
    public string LogLevel { get; set; } = "Information";
    public int EventRetentionDays { get; set; } = 30;
    public int DeliveryRetentionDays { get; set; } = 30;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
