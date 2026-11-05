using System;
using System.Linq;
using System.Net;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>One place that turns SDK failures into caller-safe <see cref="SubscriptionException"/>s.</summary>
internal static class MaxioErrors
{
    /// <summary>True when the write may still have landed at Maxio (no usable answer, or a 2xx/5xx we could not trust).</summary>
    public static bool IsUnknownOutcome(Exception ex) => ex switch
    {
        SdkConnectionException => true, // includes SdkTimeoutException
        ResponseDeserializationException r => r.StatusCode < HttpStatusCode.BadRequest || (int)r.StatusCode >= 500,
        ApiException a => (int)a.StatusCode >= 500,
        _ => false
    };

    /// <param name="detail">Optional provider validation detail to expose for a 422.</param>
    public static SubscriptionException Translate(Exception ex, string action, string? detail = null)
    {
        switch (ex)
        {
            case SubscriptionException s:
                return s;
            case ApiException api:
                var status = (int)api.StatusCode;
                if (status == 422 || status == 400)
                {
                    return new SubscriptionException(422,
                        detail is null ? $"Billing rejected the request to {action}." : $"Billing rejected the request to {action}: {detail}", ex);
                }
                if (status == 409)
                {
                    return new SubscriptionException(409, $"Billing reports a conflict while trying to {action}.", ex);
                }
                // 401/403 are our credentials, 429 our quota, 404/5xx the provider: none are the caller's fault.
                return new SubscriptionException(status == 429 ? 503 : 502, $"Billing is unavailable; could not {action}.", ex);
            case SdkTimeoutException:
                return new SubscriptionException(504, $"Billing did not answer in time; could not {action}.", ex);
            case AuthSchemeException:
                return new SubscriptionException(502, $"Billing is not configured correctly; could not {action}.", ex);
            case SdkException:
                return new SubscriptionException(502, $"Billing is unreachable; could not {action}.", ex);
            default:
                return new SubscriptionException(500, $"Unexpected error while trying to {action}.", ex);
        }
    }

    public static string? JoinErrors(System.Collections.Generic.IReadOnlyList<string>? errors) =>
        errors is { Count: > 0 } ? string.Join("; ", errors.Take(5)) : null;
}
