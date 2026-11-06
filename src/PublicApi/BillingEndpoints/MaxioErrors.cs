using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Turns failures of Maxio SDK calls into <see cref="BillingException"/>s carrying a 4xx status and the reason Maxio gave.
/// </summary>
public static class MaxioErrors
{
    /// <summary>
    /// Runs a Maxio call. <paramref name="conflictOn422"/> reports Maxio's "not allowed in this state" answers as 409.
    /// </summary>
    public static async Task<T> CallAsync<T>(Func<Task<T>> call, bool conflictOn422 = false)
    {
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            throw Translate(ex, conflictOn422);
        }
        catch (SdkException ex) when (ex is SdkConnectionException || ex is SdkTimeoutException)
        {
            throw new BillingException(503, "The billing system could not be reached.", ex);
        }
    }

    public static async Task CallAsync(Func<Task> call, bool conflictOn422 = false)
    {
        await CallAsync(async () =>
        {
            await call();
            return true;
        }, conflictOn422);
    }

    public static bool IsNotFound(ApiException ex) => (int)ex.StatusCode == 404;

    private static BillingException Translate(ApiException ex, bool conflictOn422)
    {
        var status = (int)ex.StatusCode;
        var reason = ReadReason(ex);
        switch (status)
        {
            case 404:
                return new BillingException(404, string.IsNullOrEmpty(reason) ? "Not found in the billing system." : reason, ex);
            case 400:
            case 422:
                if (string.IsNullOrEmpty(reason)) reason = "The billing system rejected the request.";
                return new BillingException(conflictOn422 ? 409 : 422, reason, ex);
            case 409:
                return new BillingException(409, string.IsNullOrEmpty(reason) ? "Conflict in the billing system." : reason, ex);
            case 401:
            case 403:
                return new BillingException(502, "The billing system refused our credentials.", ex);
            case 429:
                return new BillingException(503, "The billing system is busy, try again shortly.", ex);
            default:
                return new BillingException(502, "The billing system failed to process the request.", ex);
        }
    }

    // ApiException<TError> exposes the typed error only on the generic type, and each operation has its own
    // error type; read whichever representation the SDK parsed and flatten it to text.
    private static string ReadReason(ApiException ex)
    {
        try
        {
            var error = ex.GetType().GetProperty("Error")?.GetValue(ex);
            if (error == null) return "";
            var messages = new List<string>();
            foreach (var method in error.GetType().GetMethods().Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal) && m.GetParameters().Length == 1 && m.GetParameters()[0].IsOut))
            {
                var args = new object?[1];
                if (!(bool)method.Invoke(error, args)!) continue;
                var value = args[0];
                if (value == null) continue;
                var json = value is MaxioAdvancedBilling.Core.ErrorResponse.RawError raw
                    ? raw.ReadAsString()
                    : JsonSerializer.Serialize(value, value.GetType());
                Flatten(json, messages);
            }
            return string.Join("; ", messages.Distinct());
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static void Flatten(string json, List<string> messages)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, null, messages);
        }
        catch (JsonException)
        {
            if (!string.IsNullOrWhiteSpace(json) && json.Length < 500) messages.Add(json.Trim());
        }
    }

    private static void Walk(JsonElement e, string? key, List<string> messages)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                var text = e.GetString();
                if (!string.IsNullOrWhiteSpace(text)) messages.Add(key == null || key == "errors" || key == "error" ? text! : $"{key}: {text}");
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray()) Walk(item, key, messages);
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "additionalProperties") continue;
                    Walk(p.Value, p.Name, messages);
                }
                break;
        }
    }
}
