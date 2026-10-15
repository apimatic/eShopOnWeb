using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

// This primary key is the atomic claim, inserted BEFORE any external create.
public class BillingClaim
{
    public string Id { get; set; } = "";
    public string Site { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Reference { get; set; } = "";
    public string? ProductHandle { get; set; }
    public int? ProviderId { get; set; }
    public string State { get; set; } = "Pending";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
