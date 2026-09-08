using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Uniform error payload returned by the subscription endpoints.</summary>
public class SubscriptionErrorPayload
{
    public SubscriptionErrorPayload(string message, IReadOnlyList<string>? errors = null)
    {
        Message = message;
        Errors = errors;
    }

    public string Message { get; }

    public IReadOnlyList<string>? Errors { get; }
}
