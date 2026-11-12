using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Turns a Maxio SDK failure into a <see cref="BillingException"/> with an HTTP status and a readable reason.
/// </summary>
public static class MaxioErrorReader
{
    public static BillingException Translate(SdkException ex)
    {
        switch (ex)
        {
            case ApiException api:
                var reason = Describe(api);
                var status = (int)api.StatusCode;
                // Maxio refusing the request because of its content or state is reported as a client-side problem (4xx);
                // a Maxio server or credential failure is a gateway problem.
                if (status == 404) return new BillingException(HttpStatusCode.NotFound, reason, ex);
                if (status == 409) return new BillingException(HttpStatusCode.Conflict, reason, ex);
                if (status == 422 || status == 400) return new BillingException(HttpStatusCode.UnprocessableEntity, reason, ex);
                if (status == 429) return new BillingException(HttpStatusCode.TooManyRequests, reason, ex);
                return new BillingException(HttpStatusCode.BadGateway, $"Billing system error ({status}): {reason}", ex);
            case SdkTimeoutException:
                return new BillingException(HttpStatusCode.GatewayTimeout, "The billing system did not answer in time; the outcome is unknown, check the subscription before retrying.", ex);
            case SdkConnectionException:
                return new BillingException(HttpStatusCode.BadGateway, "The billing system could not be reached.", ex);
            default:
                return new BillingException(HttpStatusCode.BadGateway, "The billing system returned an unexpected answer.", ex);
        }
    }

    public static string Describe(ApiException ex)
    {
        try
        {
            var error = ex.GetType().GetProperty("Error")?.GetValue(ex);
            var text = error switch
            {
                null => null,
                RawError raw => raw.ReadAsString(),
                _ => DescribeTyped(error)
            };
            return Summarize(text) ?? ex.Message;
        }
        catch (Exception)
        {
            return ex.Message;
        }
    }

    private static string? DescribeTyped(object error)
    {
        // Typed (Case A) errors expose TryGet...(out payload) accessors, one per declared status, plus TryGetRawError.
        foreach (var method in error.GetType().GetMethods().Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal)))
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || !parameters[0].IsOut) continue;
            var args = new object?[] { null };
            if (method.Invoke(error, args) is true && args[0] is not null)
            {
                return args[0] is RawError raw ? raw.ReadAsString() : JsonSerializer.Serialize(args[0], args[0]!.GetType());
            }
        }
        return null;
    }

    private static string? Summarize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var messages = new List<string>();
            Collect(doc.RootElement, messages);
            if (messages.Count > 0) return string.Join(" ", messages.Distinct());
        }
        catch (JsonException)
        {
            // not JSON: fall through to the raw text
        }
        return body.Length > 500 ? body[..500] : body;
    }

    private static void Collect(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, messages);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) Collect(property.Value, messages);
                break;
        }
    }
}
