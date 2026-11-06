using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Translates SDK exceptions into <see cref="BillingException"/>s with a useful reason.
/// </summary>
public static class MaxioErrors
{
    public static BillingException Translate(Exception ex, string action)
    {
        switch (ex)
        {
            case BillingException be:
                return be;
            case ApiException api:
                var reason = Describe(api);
                var status = (int)api.StatusCode;
                if (status == 404)
                    return new BillingException(HttpStatusCode.NotFound, $"Maxio could not find the requested resource while trying to {action}.{Suffix(reason)}", ex);
                if (status == 400 || status == 409 || status == 422)
                    return new BillingException(HttpStatusCode.UnprocessableEntity, $"Maxio rejected the request to {action}.{Suffix(reason)}", ex);
                if (status == 429)
                    return new BillingException(HttpStatusCode.ServiceUnavailable, $"Maxio is rate limiting requests; could not {action}. Try again shortly.", ex);
                // 401/403 mean our own credentials are wrong; 5xx is Maxio's problem. Neither is the caller's fault.
                return new BillingException(HttpStatusCode.BadGateway, $"Maxio returned {status} while trying to {action}.{Suffix(reason)}", ex);
            case SdkTimeoutException:
                return new BillingException(HttpStatusCode.GatewayTimeout, $"Maxio did not answer in time while trying to {action}.", ex);
            case SdkConnectionException:
                return new BillingException(HttpStatusCode.ServiceUnavailable, $"Could not reach Maxio while trying to {action}.", ex);
            case AuthSchemeException:
                return new BillingException(HttpStatusCode.ServiceUnavailable, "Maxio credentials are not configured.", ex);
            default:
                return new BillingException(HttpStatusCode.BadGateway, $"Unexpected failure talking to Maxio while trying to {action}: {ex.Message}", ex);
        }
    }

    private static string Suffix(string reason) => string.IsNullOrWhiteSpace(reason) ? string.Empty : " Reason: " + reason;

    /// <summary>Digs the human readable messages out of the SDK's (operation specific) error payloads.</summary>
    public static string Describe(ApiException api)
    {
        var messages = new List<string>();
        try
        {
            var error = api.GetType().GetProperty("Error")?.GetValue(api);
            if (error != null)
            {
                var getters = error.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal)
                                && m.GetParameters().Length == 1
                                && m.GetParameters()[0].IsOut);
                foreach (var m in getters)
                {
                    var args = new object?[] { null };
                    if (m.Invoke(error, args) is true && args[0] != null)
                    {
                        if (args[0] is RawError raw)
                            Collect(raw.ReadAsString(), messages);
                        else
                            Collect(JsonSerializer.Serialize(args[0], args[0]!.GetType()), messages);
                    }
                }
            }
        }
        catch
        {
            // best effort only
        }
        return string.Join("; ", messages.Distinct());
    }

    private static void Collect(string json, List<string> messages)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, messages);
        }
        catch (JsonException)
        {
            if (!string.IsNullOrWhiteSpace(json)) messages.Add(json.Trim());
        }
    }

    private static void Walk(JsonElement e, List<string> messages)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(e.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var i in e.EnumerateArray()) Walk(i, messages);
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    var before = messages.Count;
                    Walk(p.Value, messages);
                    if (p.Name is not ("errors" or "error" or "base" or "AdditionalProperties") && messages.Count > before)
                    {
                        for (var i = before; i < messages.Count; i++) messages[i] = $"{p.Name}: {messages[i]}";
                    }
                }
                break;
        }
    }
}
