using System;

namespace Microsoft.eShopWeb.Infrastructure.Data;

// Inserted before any provider write. A pending row must never authorize another send.
public class BillingClaim
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string CustomerReference { get; set; } = "";
    public string? ProductHandle { get; set; }
    public int? ProviderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastCheckedAt { get; set; }
}
