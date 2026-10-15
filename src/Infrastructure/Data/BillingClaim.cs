using System;

namespace Microsoft.eShopWeb.Infrastructure.Data;

// Inserted BEFORE the external write. A pending claim must never be automatically re-sent.
public class BillingClaim
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Site { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Reference { get; set; } = "";
    public string? ProductHandle { get; set; }
    public int? ProviderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastCheckedAt { get; set; } = DateTimeOffset.UtcNow;
}
