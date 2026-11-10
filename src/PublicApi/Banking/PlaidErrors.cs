using System;
using System.Text.Json;
using System.Threading.Tasks;
using ThePlaidApi.Core.ErrorResponse;
using ThePlaidApi.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>The app cannot carry out the request; the middleware answers with <see cref="StatusCode"/> and the message.</summary>
public class RequestFailedException : Exception
{
    public int StatusCode { get; }

    public RequestFailedException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>What Plaid said went wrong.</summary>
public record PlaidError(string Code, string Type, string Message, int HttpStatus)
{
    public static PlaidError? From(Exception exception)
    {
        if (exception is not ApiException<RawError> api)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(api.Error.ReadAsBytes());
            string Read(string name) => doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            return new PlaidError(Read("error_code"), Read("error_type"), Read("error_message"), (int)api.StatusCode);
        }
        catch (JsonException)
        {
            return new PlaidError("", "", api.Message, (int)api.StatusCode);
        }
    }

    public static bool Is(Exception exception, string code) => From(exception)?.Code == code;

    /// <summary>Turns anything the SDK throws into the 4xx/5xx the caller should see; null when it is not a Plaid failure.</summary>
    public static RequestFailedException? Translate(Exception exception)
    {
        switch (exception)
        {
            case RequestFailedException failed:
                return failed;
            case AuthSchemeException:
                return new RequestFailedException(503, "Plaid is not configured: set Plaid:ClientId and Plaid:Secret.");
            case SdkTimeoutException:
                return new RequestFailedException(504, "Plaid did not answer in time.");
            case SdkConnectionException:
                return new RequestFailedException(502, "Plaid could not be reached.");
            case ApiException:
                break;
            case SdkException:
                return new RequestFailedException(502, "Plaid call failed: " + exception.Message);
            default:
                return null;
        }

        var error = From(exception);
        if (error is null)
        {
            return new RequestFailedException(502, "Plaid call failed: " + exception.Message);
        }

        var reason = string.IsNullOrEmpty(error.Message) ? error.Code : error.Message;
        var status = error switch
        {
            { HttpStatus: 429 } or { Code: "RATE_LIMIT_EXCEEDED" } => 429,
            { Code: "INVALID_API_KEYS" or "INVALID_CLIENT_ID" or "INVALID_SECRET" or "UNAUTHORIZED_ENVIRONMENT" or "ACCESS_NOT_GRANTED" or "PRODUCT_NOT_ENABLED" or "PRODUCTS_NOT_SUPPORTED" or "INVALID_PRODUCT" } => 503,
            { Code: "PRODUCT_NOT_READY" } => 503,
            { Code: "ITEM_NOT_FOUND" } => 404,
            { Type: "ITEM_ERROR" } => 409,
            { Type: "API_ERROR" or "ASSET_REPORT_ERROR" } => 502,
            { HttpStatus: >= 500 } => 502,
            _ => 422,
        };

        return new RequestFailedException(status, $"Plaid: {reason}");
    }

    /// <summary>Runs a Plaid call, throwing <see cref="RequestFailedException"/> for any failure.</summary>
    public static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception e) when (Translate(e) is { } failure && failure != e)
        {
            throw failure;
        }
    }

    /// <summary>
    /// Plaid prepares a product (transactions, investments) in the background the first time it is asked for;
    /// until then it answers PRODUCT_NOT_READY. Keeps asking for a while.
    /// </summary>
    public static async Task<T> WhenReady<T>(Func<Task<T>> call, int attempts = 15, int delayMs = 1500)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (Exception e) when (Is(e, "PRODUCT_NOT_READY") && attempt < attempts)
            {
                await Task.Delay(delayMs);
            }
            catch (Exception e) when (Translate(e) is { } failure && failure != e)
            {
                throw failure;
            }
        }
    }
}
