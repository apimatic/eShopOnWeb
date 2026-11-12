using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Translates SDK exceptions into <see cref="BillingException"/> (4xx with the Maxio reason where Maxio refused the request).
/// </summary>
public static class MaxioErrors
{
    public static BillingException Translate(Exception ex) => ex switch
    {
        BillingException b => b,
        ApiException<RawError> e => From(e.StatusCode, Raw(e.Error), e),
        ApiException<CreateProductFamilyError> e => From(e.StatusCode, Typed(e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null, e.Error), e),
        ApiException<CreateProductError> e => From(e.StatusCode, Typed(e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null, e.Error), e),
        ApiException<CreateSubscriptionError> e => From(e.StatusCode, Typed(e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null, e.Error), e),
        ApiException<UpdateSubscriptionError> e => From(e.StatusCode, Typed(e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null, e.Error), e),
        ApiException<PauseSubscriptionError> e => From(e.StatusCode, Typed(e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null, e.Error), e),
        ApiException<ResumeSubscriptionError> e => From(e.StatusCode, Typed(e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null, e.Error), e),
        ApiException<ReactivateSubscriptionError> e => From(e.StatusCode, Typed(e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null, e.Error), e),
        ApiException<MigrateSubscriptionProductError> e => From(e.StatusCode, Typed(e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null, e.Error), e),
        ApiException<CancelSubscriptionError> e => From(e.StatusCode, CancelReason(e.Error), e),
        ApiException<CreateProductCurrencyPricesError> e => From(e.StatusCode, CurrencyReason(e.Error), e),
        ApiException<CreateCustomerError> e => From(e.StatusCode, Typed(null, e.Error) ?? "Maxio rejected the customer details.", e),
        // A body that does not match its declared type: the server answered, so keep the status.
        ApiException e => From(e.StatusCode, null, e),
        SdkTimeoutException e => new BillingException(504, "Maxio did not answer in time.", e),
        SdkConnectionException e => new BillingException(502, "Maxio could not be reached.", e),
        AuthSchemeException e => new BillingException(503, "Billing credentials are not configured.", e),
        SdkException e => new BillingException(502, "Billing request failed.", e),
        _ => new BillingException(500, ex.Message, ex)
    };

    private static BillingException From(HttpStatusCode status, string? reason, Exception inner)
    {
        var code = (int)status;
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason;
        return code switch
        {
            404 => new BillingException(404, reason ?? "Not found in Maxio.", inner),
            409 => new BillingException(409, reason ?? "Conflict in Maxio.", inner),
            400 or 422 => new BillingException(422, reason ?? "Maxio rejected the request.", inner),
            // Credentials rejected / throttled / upstream fault: not the caller's doing.
            429 => new BillingException(503, "Maxio is throttling requests; try again shortly.", inner),
            401 or 403 => new BillingException(502, "Maxio rejected the application's credentials.", inner),
            _ => new BillingException(502, "Maxio failed to process the request.", inner)
        };
    }

    private static string? Typed(System.Collections.Generic.IReadOnlyList<string>? errors, ApiError error)
    {
        if (errors is { Count: > 0 }) return string.Join(" ", errors);
        return error.TryGetRawError(out var raw) ? Raw(raw) : null;
    }

    private static string? CancelReason(CancelSubscriptionError error)
    {
        if (error.TryGetCancelSubscriptionErrorResponse(out var body))
        {
            if (body.TryGetErrorListResponse1(out var list)) return string.Join(" ", list.Errors);
            if (body.TryGetSingleErrorResponse1(out var single)) return single.Error;
        }
        return error.TryGetRawError(out var raw) ? Raw(raw) : null;
    }

    private static string? CurrencyReason(CreateProductCurrencyPricesError error)
    {
        if (error.TryGetErrorArrayMapResponse1(out var body) && body.Errors is { Count: > 0 })
        {
            return string.Join(" ", body.Errors.SelectMany(kv => Flatten(kv.Value)).Distinct());
        }
        return error.TryGetRawError(out var raw) ? Raw(raw) : null;
    }

    private static System.Collections.Generic.IEnumerable<string> Flatten(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.Array } arr => arr.EnumerateArray().Select(x => x.ToString()),
        null => Array.Empty<string>(),
        _ => new[] { value.ToString() ?? string.Empty }
    };

    private static string? Raw(RawError raw)
    {
        var text = raw.ReadAsString();
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "errors", "error" })
                {
                    if (doc.RootElement.TryGetProperty(name, out var el))
                    {
                        return el.ValueKind switch
                        {
                            JsonValueKind.Array => string.Join(" ", el.EnumerateArray().Select(x => x.ToString())),
                            JsonValueKind.Object => string.Join(" ", el.EnumerateObject().SelectMany(p => Flatten(p.Value))),
                            _ => el.ToString()
                        };
                    }
                }
            }
        }
        catch (JsonException)
        {
            // not JSON — fall through to the trimmed text
        }
        text = text.Trim();
        return text.Length > 300 ? text[..300] : text;
    }
}
