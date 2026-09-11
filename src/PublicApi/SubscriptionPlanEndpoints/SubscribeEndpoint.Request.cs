using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string ProductHandle { get; set; } = "";
    public SubscribeRequest() { }
    public SubscribeRequest(Guid correlationId, string productHandle) { ProductHandle = productHandle; }
}

public class SubscribeResponse : BaseResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public string? NextBillingDate { get; set; }
    public bool Created { get; set; }
    public SubscribeResponse(Guid correlationId)  { }
}
