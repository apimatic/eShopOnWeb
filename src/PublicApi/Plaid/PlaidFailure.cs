using System;
using System.Text.Json;
using ThePlaidApi.Core.ErrorResponse;
using ThePlaidApi.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>Turns SDK / Plaid failures into API answers with a status and a reason.</summary>
public static class PlaidFailure
{
    public sealed record Details(string? Type, string? Code, string? Message);

    public static Details? Read(Exception e)
    {
        if (e is not ApiException<RawError> api) return null;
        try
        {
            using var doc = JsonDocument.Parse(api.Error.ReadAsBytes());
            string? S(string n) => doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return new Details(S("error_type"), S("error_code"), S("display_message") ?? S("error_message"));
        }
        catch (JsonException)
        {
            return new Details(null, null, null);
        }
    }

    public static bool HasCode(Exception e, params string[] codes)
    {
        var d = Read(e);
        return d?.Code is { } c && Array.IndexOf(codes, c) >= 0;
    }

    /// <summary>Maps an exception coming from the Plaid SDK; null when it is not a Plaid failure.</summary>
    public static BankApiException? ToApiException(Exception e)
    {
        switch (e)
        {
            case BankApiException b:
                return b;
            case ApiException<RawError> api:
                var d = Read(api);
                var reason = d?.Message ?? "Plaid rejected the request.";
                var status = (int)api.StatusCode;
                if (status == 429)
                    return new BankApiException(429, "Plaid is rate limiting requests, please try again shortly.", e);
                if (status >= 500)
                    return new BankApiException(502, $"Plaid is unavailable: {reason}", e);
                if (d?.Type == "ITEM_ERROR")
                    return new BankApiException(409, $"The bank connection can't be used right now ({d.Code}): {reason}", e);
                if (status == 401 || status == 403)
                    return new BankApiException(503, "The shop's Plaid credentials were rejected.", e);
                return new BankApiException(422, reason, e);
            case SdkTimeoutException:
                return new BankApiException(504, "Plaid did not answer in time.", e);
            case SdkConnectionException:
                return new BankApiException(502, "Plaid could not be reached.", e);
            case AuthSchemeException:
                return new BankApiException(503, "Plaid is not configured: set Plaid:ClientId and Plaid:Secret.", e);
            case ApiException:
                return new BankApiException(502, "Plaid returned an unreadable answer.", e);
            default:
                return null;
        }
    }
}
