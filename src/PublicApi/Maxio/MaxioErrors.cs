using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Turns SDK exceptions into <see cref="BillingException"/>s a caller can act on.</summary>
public static class MaxioErrors
{
    public static BillingException Translate(SdkException ex, bool isWrite)
    {
        switch (ex)
        {
            case ApiException api:
                return FromApi(api);
            case SdkTimeoutException:
                return new BillingException(HttpStatusCode.GatewayTimeout,
                    "The billing system did not answer in time.", ex, isWrite);
            case SdkConnectionException:
                return new BillingException(HttpStatusCode.BadGateway,
                    "The billing system could not be reached.", ex, isWrite);
            default:
                return new BillingException(HttpStatusCode.BadGateway,
                    "The billing system call failed: " + ex.GetType().Name, ex);
        }
    }

    private static BillingException FromApi(ApiException api)
    {
        var reason = Describe(api);
        var status = (int)api.StatusCode;
        return status switch
        {
            404 => new BillingException(HttpStatusCode.NotFound, Prefix("Not found in the billing system", reason), api),
            409 => new BillingException(HttpStatusCode.Conflict, Prefix("Conflict in the billing system", reason), api),
            400 or 422 => new BillingException(HttpStatusCode.UnprocessableEntity,
                Prefix("The billing system refused the request", reason), api),
            401 or 403 => new BillingException(HttpStatusCode.BadGateway,
                "The billing system rejected this application's credentials.", api),
            429 => new BillingException(HttpStatusCode.TooManyRequests,
                "The billing system is rate limiting requests; try again later.", api),
            >= 400 and < 500 => new BillingException(HttpStatusCode.UnprocessableEntity,
                Prefix("The billing system refused the request", reason), api),
            _ => new BillingException(HttpStatusCode.BadGateway,
                Prefix($"The billing system failed ({status})", reason), api),
        };
    }

    private static string Prefix(string head, string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? head + "." : head + ": " + reason;

    /// <summary>Reads the provider's reason out of whichever typed or raw error body the operation declares.</summary>
    public static string? Describe(ApiException api)
    {
        try
        {
            var error = api.GetType().GetProperty("Error")?.GetValue(api);
            switch (error)
            {
                case null:
                    return null;
                case RawError raw:
                    return Clip(raw.ReadAsString());
                case ApiError typed:
                    foreach (var method in typed.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                 .Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal)
                                             && m.Name != "TryGetRawError"
                                             && m.GetParameters().Length == 1
                                             && m.GetParameters()[0].IsOut))
                    {
                        var args = new object?[1];
                        if (method.Invoke(typed, args) is true && args[0] is not null)
                            return Format(args[0]!);
                    }
                    if (typed.TryGetRawError(out var fallback))
                        return Clip(fallback.ReadAsString());
                    return null;
                default:
                    return Format(error);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Format(object payload) => payload switch
    {
        ErrorListResponse1 list => Clip(string.Join("; ", list.Errors)),
        string s => Clip(s),
        RawError raw => Clip(raw.ReadAsString()),
        _ => Clip(JsonSerializer.Serialize(payload, payload.GetType())),
    };

    private static string? Clip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        return text.Length > 500 ? text[..500] + "…" : text;
    }
}
