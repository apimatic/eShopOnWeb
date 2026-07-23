using System;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewRequest : BaseRequest
{
    /// <summary>The durable handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; } = string.Empty;
}

public class PlanChangePreviewResponse : BaseResponse
{
    public PlanChangePreviewResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangePreviewResponse()
    {
    }

    public PlanChangePreviewDto? Preview { get; set; }
}

public class PlanChangeRequestDto : BaseRequest
{
    /// <summary>Taken from the route, never from the request body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// <c>Immediately</c> applies the change now with proration; <c>AtNextRenewal</c> defers it to the
    /// next renewal with no proration.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PlanChangeTiming Timing { get; set; }

    /// <summary>The previewed net amount due, in cents. Required for an immediate change.</summary>
    public long? ConfirmedPaymentDueInCents { get; set; }

    /// <summary>When that preview was produced. Required for an immediate change.</summary>
    public DateTimeOffset? PreviewedAt { get; set; }
}

public class PlanChangeResponse : BaseResponse
{
    public PlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangeResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}
