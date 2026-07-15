namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public double Quantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>Server-assigned from the authenticated principal — never bound from client input.</summary>
    public string? UserId { get; set; }

    /// <summary>Server-assigned from the authenticated principal's role — never bound from client input.</summary>
    public bool ActingAsAdmin { get; set; }
}
