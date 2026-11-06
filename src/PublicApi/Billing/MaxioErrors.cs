using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Runs Maxio calls and translates SDK failures into <see cref="BillingException"/>s with a reason.
/// </summary>
public static class MaxioErrors
{
    public static async Task<T> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new BillingException(HttpStatusCode.GatewayTimeout, "The billing system did not answer in time.", ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new BillingException(HttpStatusCode.BadGateway, "The billing system could not be reached.", ex);
        }
        catch (SdkException ex)
        {
            throw new BillingException(HttpStatusCode.BadGateway, "The billing system returned an unusable answer.", ex);
        }
    }

    public static async Task Run(Func<Task> call)
    {
        await Run(async () =>
        {
            await call();
            return true;
        });
    }

    /// <summary>True when the SDK failure is an HTTP 404 from Maxio.</summary>
    public static bool IsNotFound(Exception ex) => ex is ApiException api && api.StatusCode == HttpStatusCode.NotFound;

    private static BillingException Translate(ApiException ex)
    {
        var reason = ExtractReason(ex);
        var status = (int)ex.StatusCode;
        return status switch
        {
            404 => new BillingException(HttpStatusCode.NotFound, Describe("Not found in the billing system", reason), ex),
            400 or 409 or 422 => new BillingException(HttpStatusCode.UnprocessableEntity, Describe("The billing system rejected the request", reason), ex),
            // Anything else (bad credentials, rate limits, outages) is not the caller's doing.
            429 => new BillingException(HttpStatusCode.TooManyRequests, "The billing system is busy, try again shortly.", ex),
            _ => new BillingException(HttpStatusCode.BadGateway, $"The billing system failed ({status}).", ex)
        };
    }

    private static string Describe(string prefix, string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? prefix + "." : $"{prefix}: {reason}";

    private static string? ExtractReason(ApiException ex)
    {
        var errorProp = ex.GetType().GetProperty("Error");
        var error = errorProp?.GetValue(ex);
        if (error == null)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var method in error.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                     .Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal) && m.GetParameters().Length == 1))
        {
            var args = new object?[] { null };
            if (method.Invoke(error, args) is true && args[0] != null)
            {
                var value = args[0]!;
                try
                {
                    var json = value is RawError raw ? raw.ReadAsString() : JsonSerializer.Serialize(value, value.GetType());
                    CollectStrings(json, parts);
                }
                catch (Exception)
                {
                    // best-effort reason extraction
                }
            }
        }

        return parts.Count == 0 ? null : string.Join("; ", parts.Distinct());
    }

    private static void CollectStrings(string json, List<string> parts)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, null, parts);
        }
        catch (JsonException)
        {
            parts.Add(json.Length > 300 ? json[..300] : json);
        }
    }

    private static void Walk(JsonElement el, string? name, List<string> parts)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    parts.Add(name == null || name == "errors" || name == "error" ? s! : $"{name}: {s}");
                }
                break;
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    Walk(p.Value, p.Name, parts);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    Walk(item, name, parts);
                }
                break;
        }
    }
}
