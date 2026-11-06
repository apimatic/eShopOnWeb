using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>Translates SDK failures into <see cref="BillingException"/>s with a readable reason.</summary>
internal static class MaxioErrors
{
    public static BillingException Translate(Exception ex)
    {
        switch (ex)
        {
            case BillingException be:
                return be;
            case ApiException api:
                return FromApi(api);
            case SdkTimeoutException:
                return new BillingException(HttpStatusCode.GatewayTimeout, "The billing system did not respond in time.", ex);
            case SdkConnectionException:
                return new BillingException(HttpStatusCode.BadGateway, "The billing system could not be reached.", ex);
            default:
                return new BillingException(HttpStatusCode.BadGateway, "The billing system call failed: " + ex.Message, ex);
        }
    }

    private static BillingException FromApi(ApiException api)
    {
        var status = (int)api.StatusCode;
        var reason = ExtractReason(api);
        switch (status)
        {
            case 404:
                return new BillingException(HttpStatusCode.NotFound, reason ?? "Not found in the billing system.", api);
            case 409:
                return new BillingException(HttpStatusCode.Conflict, reason ?? "Conflict in the billing system.", api);
            case 400:
            case 422:
                return new BillingException(status == 400 ? HttpStatusCode.BadRequest : HttpStatusCode.UnprocessableEntity,
                    reason ?? "The billing system rejected the request.", api);
            case 401:
            case 403:
                return new BillingException(HttpStatusCode.BadGateway, "The billing system refused our credentials.", api);
            case 429:
                return new BillingException(HttpStatusCode.ServiceUnavailable, "The billing system is rate limiting requests; try again shortly.", api);
            default:
                return new BillingException(HttpStatusCode.BadGateway, $"The billing system failed ({status}).", api);
        }
    }

    /// <summary>
    /// The SDK exposes the typed error body through endpoint-specific TryGet* methods; read whichever one is populated.
    /// </summary>
    private static string? ExtractReason(ApiException api)
    {
        try
        {
            var error = api.GetType().GetProperty("Error")?.GetValue(api);
            if (error is null) return null;
            foreach (var m in error.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal)
                                     && m.Name != "TryGetRawError"
                                     && m.GetParameters().Length == 1 && m.GetParameters()[0].IsOut))
            {
                var args = new object?[1];
                if (m.Invoke(error, args) is true && args[0] is { } value)
                    return Describe(value);
            }
            var raw = error.GetType().GetMethod("TryGetRawError");
            if (raw is not null)
            {
                var args = new object?[1];
                if (raw.Invoke(error, args) is true && args[0] is { } r)
                {
                    var text = r.GetType().GetMethod("ReadAsString")?.Invoke(r, null) as string;
                    if (!string.IsNullOrWhiteSpace(text)) return Truncate(text);
                }
            }
        }
        catch
        {
            // fall through to a generic reason
        }
        return null;
    }

    private static string Describe(object value)
    {
        if (value.GetType().GetProperty("Errors")?.GetValue(value) is System.Collections.IEnumerable list and not string)
        {
            var items = list.Cast<object?>().Select(o => o?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s));
            var joined = string.Join("; ", items);
            if (joined.Length > 0) return Truncate(joined);
        }
        return Truncate(JsonSerializer.Serialize(value, value.GetType()));
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s.Substring(0, 500);
}
