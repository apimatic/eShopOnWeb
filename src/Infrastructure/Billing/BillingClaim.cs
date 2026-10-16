using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

// Primary key inserts precede every external write; unknown outcomes are never re-posted.
public class BillingClaim
{
    public string Id { get; set; } = "";
    public string Scope { get; set; } = "";
    public string UserId { get; set; } = "";
    public string CustomerReference { get; set; } = "";
    public string Reference { get; set; } = "";
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public int? ProviderId { get; set; }
    public string State { get; set; } = "Claimed";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
