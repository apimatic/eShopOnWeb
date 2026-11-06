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
/// Runs a Maxio SDK call and translates SDK failures into <see cref="BillingException"/>s that
/// carry a 4xx/5xx status and the reason Maxio gave.
/// </summary>
internal static class MaxioCall
{
    public static async Task<T> RunAsync<T>(Func<Task<T>> call, string notFoundMessage = "The requested billing resource was not found.")
    {
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            throw Translate(ex, notFoundMessage);
        }
        catch (SdkException ex)
        {
            throw new BillingException(503, "The billing system could not be reached. Please try again later.", ex);
        }
    }

    public static async Task RunAsync(Func<Task> call, string notFoundMessage = "The requested billing resource was not found.")
    {
        await RunAsync(async () => { await call(); return true; }, notFoundMessage);
    }

    public static bool IsNotFound(Exception ex) => ex is ApiException { StatusCode: HttpStatusCode.NotFound };

    public static string Reason(ApiException ex) => ExtractReason(ex) ?? "The billing system rejected the request.";

    private static BillingException Translate(ApiException ex, string notFoundMessage)
    {
        var status = (int)ex.StatusCode;
        var reason = ExtractReason(ex);
        switch (status)
        {
            case 404:
                return new BillingException(404, notFoundMessage, ex);
            case 401:
            case 403:
                return new BillingException(502, "The billing system rejected this application's credentials.", ex);
            case 429:
                return new BillingException(503, "The billing system is rate limiting requests. Please try again later.", ex);
            case >= 500:
                return new BillingException(502, "The billing system failed to process the request. Please try again later.", ex);
            case 400:
            case 422:
                return new BillingException(422, reason ?? "The billing system rejected the request.", ex);
            default:
                return new BillingException(status, reason ?? "The billing system rejected the request.", ex);
        }
    }

    private static string? ExtractReason(ApiException ex)
    {
        var errorProp = ex.GetType().GetProperty("Error");
        var error = errorProp?.GetValue(ex);
        if (error == null) return null;

        // Typed error payloads are exposed through TryGetXxx(out T) accessors.
        foreach (var method in error.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!method.Name.StartsWith("TryGet", StringComparison.Ordinal) || method.Name == "TryGetRawError") continue;
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || !parameters[0].IsOut) continue;
            var args = new object?[] { null };
            if (method.Invoke(error, args) is true && args[0] != null)
            {
                var text = Flatten(JsonSerializer.SerializeToElement(args[0], args[0]!.GetType()));
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }

        if (error is ApiError apiError && apiError.TryGetRawError(out var raw))
        {
            var body = raw.ReadAsString();
            try
            {
                using var doc = JsonDocument.Parse(body);
                var text = Flatten(doc.RootElement);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch (JsonException)
            {
            }
            return string.IsNullOrWhiteSpace(body) ? null : body.Length > 500 ? body[..500] : body;
        }

        return null;
    }

    private static string? Flatten(JsonElement element)
    {
        var parts = new List<string>();
        Collect(element, parts);
        var distinct = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
        return distinct.Count == 0 ? null : string.Join(" ", distinct);
    }

    private static void Collect(JsonElement e, List<string> parts)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                parts.Add(e.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray()) Collect(item, parts);
                break;
            case JsonValueKind.Object:
                foreach (var prop in e.EnumerateObject())
                {
                    if (prop.Name == "AdditionalProperties") continue;
                    Collect(prop.Value, parts);
                }
                break;
        }
    }
}
