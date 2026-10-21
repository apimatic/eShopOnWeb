using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Caller-safe error body for the subscription endpoints. Never carries SDK or exception detail.</summary>
public class SubscriptionErrorResponse : BaseResponse
{
    public SubscriptionErrorResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionErrorResponse()
    {
    }

    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Validation messages from the billing system, when it declined the request.</summary>
    public List<string> Errors { get; set; } = new List<string>();
}
