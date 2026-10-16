using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

// The primary key is acquired BEFORE a provider write. Never expire an ambiguous write into a retry.
public class BillingClaim
{
    public string Id { get; set; } = "";
    public string Scope { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Reference { get; set; } = "";
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public int? RemoteId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastCheckedAt { get; set; }
}
