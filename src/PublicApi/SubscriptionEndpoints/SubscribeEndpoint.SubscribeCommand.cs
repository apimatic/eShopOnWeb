using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribe request joined to the caller's authenticated identity. The email comes from the bearer
/// token, so a caller cannot subscribe on someone else's behalf by shaping the body.
/// </summary>
public class SubscribeCommand : BaseRequest
{
    public SubscribeCommand(Guid correlationId, string subscriberEmail, string? planHandle)
    {
        _correlationId = correlationId;
        SubscriberEmail = subscriberEmail;
        PlanHandle = planHandle;
    }

    public string SubscriberEmail { get; }

    public string? PlanHandle { get; }
}
