using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>Translates SDK failures into <see cref="BillingException"/>s carrying a reason.</summary>
public static class MaxioErrors
{
    public static async Task<T> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (BillingException)
        {
            throw;
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new BillingException(504, "The billing system timed out.", ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new BillingException(503, "The billing system could not be reached.", ex);
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

    /// <summary>True when Maxio answered 404 for the call.</summary>
    public static bool IsNotFound(Exception ex) =>
        ex is BillingException { StatusCode: 404 };

    private static BillingException Translate(ApiException ex)
    {
        var status = (int)ex.StatusCode;
        var reason = ExtractReason(ex);
        return status switch
        {
            404 => new BillingException(404, reason ?? "Not found in the billing system.", ex),
            401 or 403 => new BillingException(502, "The billing system rejected the app's credentials.", ex),
            >= 400 and < 500 => new BillingException(
                status == 429 ? 503 : (status == 409 ? 409 : 422),
                reason ?? $"The billing system rejected the request ({status}).", ex),
            _ => new BillingException(502, $"The billing system failed ({status}).", ex),
        };
    }

    private static string? ExtractReason(ApiException ex)
    {
        var reasons = new List<string>();
        try
        {
            var error = ex.GetType().GetProperty("Error")?.GetValue(ex);
            if (error != null)
            {
                Collect(error, reasons, 0);
            }
        }
        catch
        {
            // best effort only
        }
        return reasons.Count == 0 ? null : string.Join("; ", reasons.Distinct());
    }

    private static void Collect(object error, List<string> reasons, int depth)
    {
        if (depth > 3) return;
        // Typed errors expose TryGetXxx(out value) accessors; probe each.
        foreach (var m in error.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                     .Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal)))
        {
            var ps = m.GetParameters();
            if (ps.Length != 1 || !ps[0].IsOut) continue;
            var args = new object?[] { null };
            if (m.Invoke(error, args) is true && args[0] is { } value)
            {
                Collect(value, reasons, depth + 1);
            }
        }

        if (error is MaxioAdvancedBilling.Core.ErrorResponse.RawError raw)
        {
            AddFromJson(raw.ReadAsString(), reasons);
            return;
        }

        foreach (var name in new[] { "Errors", "Error", "Message" })
        {
            var p = error.GetType().GetProperty(name);
            if (p != null) AddValue(p.GetValue(error), reasons, 0);
        }

        // Errors keyed by field (e.g. {"subscription": {"base": ["..."]}}) land in AdditionalProperties.
        if (error.GetType().GetProperty("AdditionalProperties")?.GetValue(error) is IEnumerable props)
        {
            foreach (var kv in props)
            {
                var val = kv?.GetType().GetProperty("Value")?.GetValue(kv);
                if (val is JsonElement je) AddFromJson(je.GetRawText(), reasons);
                else if (val != null) reasons.Add(val.ToString()!);
            }
        }
    }

    /// <summary>Collects strings from a string, a sequence, or a map (errors keyed by field).</summary>
    private static void AddValue(object? value, List<string> reasons, int depth)
    {
        if (value == null || depth > 4) return;
        switch (value)
        {
            case string s:
                if (!string.IsNullOrWhiteSpace(s)) reasons.Add(s);
                break;
            case JsonElement je:
                AddFromJson(je.GetRawText(), reasons);
                break;
            case IEnumerable e:
                foreach (var item in e)
                {
                    var itemType = item?.GetType();
                    if (itemType is { IsGenericType: true } && itemType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                        AddValue(itemType.GetProperty("Value")!.GetValue(item), reasons, depth + 1);
                    else
                        AddValue(item, reasons, depth + 1);
                }
                break;
        }
    }

    private static void AddFromJson(string text, List<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            using var doc = JsonDocument.Parse(text);
            Walk(doc.RootElement, reasons);
        }
        catch (JsonException)
        {
            reasons.Add(text.Length > 300 ? text[..300] : text);
        }
    }

    private static void Walk(JsonElement e, List<string> reasons)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                reasons.Add(e.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var i in e.EnumerateArray()) Walk(i, reasons);
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject()) Walk(p.Value, reasons);
                break;
        }
    }
}
