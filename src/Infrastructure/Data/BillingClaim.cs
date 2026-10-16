namespace Microsoft.eShopWeb.Infrastructure.Data;

// The primary key is the atomic reservation, including with EF's in-memory provider.
public class BillingClaim
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public string Reference { get; set; } = "";
    public int? ProviderId { get; set; }
    public string State { get; set; } = "pending";
}
