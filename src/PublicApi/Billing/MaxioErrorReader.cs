using System;
using System.Linq;
using System.Reflection;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Turns what the Maxio SDK throws into a <see cref="BillingException"/> carrying Maxio's own reason.
/// </summary>
public static class MaxioErrorReader
{
    public static BillingException ToBillingException(SdkException ex)
    {
        switch (ex)
        {
            case ApiException api:
                var reason = ReadReason(api);
                var status = (int)api.StatusCode;
                return status switch
                {
                    404 => new BillingException(404, $"Not found in the billing system. {reason}".Trim(), ex),
                    400 or 409 or 422 => new BillingException(status == 400 ? 422 : status,
                        string.IsNullOrWhiteSpace(reason) ? "The billing system refused the request." : reason, ex),
                    429 => new BillingException(503, "The billing system is busy; try again shortly.", ex),
                    _ => new BillingException(502, $"The billing system could not process the request (HTTP {status}). {reason}".Trim(), ex),
                };
            case SdkTimeoutException:
            case SdkConnectionException:
                return new BillingException(504, "The billing system did not answer in time.", ex);
            default:
                // Includes ResponseDeserializationException and AuthSchemeException: a fault on our side of the integration.
                return new BillingException(502, "The billing system returned an unusable answer.", ex);
        }
    }

    private static string ReadReason(ApiException ex)
    {
        try
        {
            var error = ex.GetType().GetProperty("Error")?.GetValue(ex);
            if (error is null)
            {
                return string.Empty;
            }
            if (error is RawError raw)
            {
                return Trim(raw.ReadAsString());
            }
            // Typed error (Case A): ask each TryGet… accessor; the first present payload wins.
            foreach (var method in error.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal) && m.Name != "TryGetRawError" && m.GetParameters().Length == 1))
            {
                var args = new object?[] { null };
                if (method.Invoke(error, args) is true && args[0] is { } payload)
                {
                    var text = Describe(payload);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
            var rawArgs = new object?[] { null };
            if (error.GetType().GetMethod("TryGetRawError")?.Invoke(error, rawArgs) is true && rawArgs[0] is RawError r)
            {
                return Trim(r.ReadAsString());
            }
        }
        catch (Exception)
        {
            // The reason is best effort; the status already says what happened.
        }
        return string.Empty;
    }

    private static string Describe(object payload) => payload switch
    {
        ErrorListResponse1 list => string.Join(" ", list.Errors),
        SingleErrorResponse1 single => single.Error,
        ErrorArrayMapResponse1 map when map.Errors is not null =>
            string.Join(" ", map.Errors.Select(kv => $"{kv.Key}: {FlattenValue(kv.Value)}")),
        string s => s,
        RawError raw => Trim(raw.ReadAsString()),
        _ => Trim(System.Text.Json.JsonSerializer.Serialize(payload, payload.GetType())),
    };

    private static string FlattenValue(object value)
    {
        if (value is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            return string.Join(", ", el.EnumerateArray().Select(e => e.ToString()).Distinct());
        }
        return value.ToString() ?? string.Empty;
    }

    private static string Trim(string s)
    {
        s = s.Trim();
        return s.Length > 500 ? s.Substring(0, 500) : s;
    }
}
