using System;

namespace Microsoft.eShopWeb.Infrastructure.Data;

// Inserted before any remote create. A pending claim is never permission to resend.
public sealed class BillingClaim
{
    public string Id { get; set; } = "";
    public int? ProviderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
