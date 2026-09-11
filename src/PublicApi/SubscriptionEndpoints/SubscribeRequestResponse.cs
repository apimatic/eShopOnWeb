using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    [Required] public string ProductHandle { get; set; } = string.Empty;
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeResponse() { }
    public bool Success { get; set; }
    public string? PlanHandle { get; set; }
    public string? State { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public string? Error { get; set; }
}
