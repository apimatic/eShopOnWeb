using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class SubscriptionProvisioningIntent
{
    public int Id { get; set; }

    public string UserReference { get; set; } = string.Empty;

    public string ProductHandle { get; set; } = string.Empty;

    public string SubscriptionReference { get; set; } = string.Empty;

    public string LeaseToken { get; set; } = string.Empty;

    public DateTimeOffset LeaseExpiresAt { get; set; }

    public int? MaxioSubscriptionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; }
}
