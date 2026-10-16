using System;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public sealed class SubscriptionEnrollmentClaim
{
    public string Reference { get; set; } = "";
    public string UserId { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public int? MaxioSubscriptionId { get; set; }
    public string State { get; set; } = BillingClaimStates.Pending;
    public DateTimeOffset ClaimedAt { get; set; }
}
