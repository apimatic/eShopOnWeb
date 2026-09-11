using System.ComponentModel.DataAnnotations;
namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
public class SubscribeRequest
{
    [Required]
    public string PlanHandle { get; set; } = "eshop-pro";
}
public class SubscribeResponse
{
    public int SubscriptionId { get; set; }
    public string CustomerReference { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public string State { get; set; } = "";
    public string NextBillingDate { get; set; } = "";
}
