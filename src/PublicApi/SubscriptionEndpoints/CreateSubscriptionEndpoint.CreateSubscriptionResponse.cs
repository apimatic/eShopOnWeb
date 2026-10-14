using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>created | existing | pending | conflict | invalid_plan | rejected</summary>
    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }

    public List<string>? Errors { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}
