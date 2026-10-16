using System;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public static class BillingClaimStates
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";
    public const string Unknown = "Unknown";
}

public sealed class MaxioCustomerClaim
{
    public string UserId { get; set; } = "";
    public int? MaxioCustomerId { get; set; }
    public string State { get; set; } = BillingClaimStates.Pending;
    public DateTimeOffset ClaimedAt { get; set; }
}
