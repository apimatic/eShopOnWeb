using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>Turns SDK failures into a status code and a reason for the caller.</summary>
public static class MaxioErrors
{
    public static BillingException Translate(Exception ex, string action)
    {
        switch (ex)
        {
            case BillingException be:
                return be;
            case ApiException api:
                var status = (int)api.StatusCode;
                var reason = Describe(api);
                if (status == 401 || status == 403)
                {
                    return new BillingException(502, $"Could not {action}: the billing system rejected this application's credentials.", ex);
                }
                if (status >= 400 && status < 500)
                {
                    return new BillingException(status, $"Could not {action}: {reason}", ex);
                }
                return new BillingException(502, $"Could not {action}: the billing system failed ({status}). {reason}", ex);
            case SdkTimeoutException:
                return new BillingException(504, $"Could not {action}: the billing system did not answer in time.", ex);
            case SdkConnectionException:
                return new BillingException(502, $"Could not {action}: the billing system is unreachable.", ex);
            case SdkException:
                return new BillingException(502, $"Could not {action}: the billing system answered unexpectedly.", ex);
            default:
                return new BillingException(502, $"Could not {action}: {ex.Message}", ex);
        }
    }

    private static string Describe(ApiException api)
    {
        try
        {
            var error = api.GetType().GetProperty("Error")?.GetValue(api);
            var texts = new List<string>();
            if (error is RawError raw)
            {
                texts.Add(raw.ReadAsString());
            }
            else if (error is ApiError typed)
            {
                if (typed.TryGetRawError(out var fallback))
                {
                    texts.Add(fallback.ReadAsString());
                }

                foreach (var m in typed.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                             .Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal) && m.Name != "TryGetRawError"))
                {
                    var ps = m.GetParameters();
                    if (ps.Length != 1 || !ps[0].IsOut) continue;
                    var args = new object?[] { null };
                    if (m.Invoke(typed, args) is true && args[0] != null)
                    {
                        texts.Add(args[0] is RawError r ? r.ReadAsString() : JsonSerializer.Serialize(args[0], args[0]!.GetType()));
                    }
                }
            }

            var messages = texts.SelectMany(Flatten).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            if (messages.Count > 0)
            {
                return string.Join("; ", messages);
            }
        }
        catch (Exception)
        {
            // fall through to the generic reason
        }

        return $"the billing system answered {(int)api.StatusCode}.";
    }

    private static IEnumerable<string> Flatten(string text)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return new[] { text.Trim() };
        }

        var result = new List<string>();
        using (doc)
        {
            Collect(doc.RootElement, null, result);
        }
        return result;
    }

    private static void Collect(JsonElement e, string? key, List<string> result)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                result.Add(string.IsNullOrEmpty(key) || key is "errors" or "error" ? e.GetString()! : $"{key}: {e.GetString()}");
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray()) Collect(item, key, result);
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "AdditionalProperties") continue;
                    Collect(p.Value, p.Name, result);
                }
                break;
        }
    }
}
