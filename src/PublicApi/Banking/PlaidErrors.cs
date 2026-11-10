using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using ThePlaidApi.Core.ErrorResponse;
using ThePlaidApi.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>An expected failure: the app can't carry out the request. Answered with the status and reason.</summary>
public class ProblemException : Exception
{
    public ProblemException(HttpStatusCode statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed record PlaidError(string? Type, string? Code, string? Message);

public static class PlaidCall
{
    /// <summary>Runs a Plaid call and turns Plaid / transport failures into <see cref="ProblemException"/>s.</summary>
    public static async Task<T> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (ApiException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (ApiException ex)
        {
            throw new ProblemException(HttpStatusCode.BadGateway, $"Plaid returned HTTP {(int)ex.StatusCode}.", ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new ProblemException(HttpStatusCode.GatewayTimeout, "Plaid did not answer in time.", ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new ProblemException(HttpStatusCode.BadGateway, "Plaid could not be reached.", ex);
        }
    }

    public static async Task Run(Func<Task> call) =>
        await Run(async () =>
        {
            await call().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);

    public static PlaidError? Parse(ApiException<RawError> ex)
    {
        try
        {
            using var doc = JsonDocument.Parse(ex.Error.ReadAsBytes());
            var root = doc.RootElement;
            string? Get(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return new PlaidError(Get("error_type"), Get("error_code"), Get("error_message"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool HasCode(Exception ex, string code) =>
        ex is ProblemException { InnerException: ApiException<RawError> inner } && Parse(inner)?.Code == code;

    public static bool IsPlaidCode(ApiException<RawError> ex, string code) => Parse(ex)?.Code == code;

    private static ProblemException Translate(ApiException<RawError> ex)
    {
        var error = Parse(ex);
        var reason = string.IsNullOrWhiteSpace(error?.Message) ? $"Plaid returned HTTP {(int)ex.StatusCode}." : error!.Message!;
        var message = $"Plaid: {reason}" + (error?.Code is null ? string.Empty : $" ({error.Code})");

        if ((int)ex.StatusCode == 429 || error?.Type == "RATE_LIMIT_EXCEEDED")
            return new ProblemException(HttpStatusCode.TooManyRequests, message, ex);

        return error?.Type switch
        {
            // The shopper's bank link needs attention (login required, consent, ...).
            "ITEM_ERROR" => new ProblemException(HttpStatusCode.Conflict, message, ex),
            "INVALID_REQUEST" or "INVALID_INPUT" => new ProblemException(HttpStatusCode.UnprocessableEntity, message, ex),
            "TRANSFER_ERROR" or "INSTITUTION_ERROR" => new ProblemException(HttpStatusCode.UnprocessableEntity, message, ex),
            _ => new ProblemException(HttpStatusCode.BadGateway, message, ex)
        };
    }

    /// <summary>
    /// Retries while Plaid is still preparing a product's data for a freshly linked item.
    /// </summary>
    public static async Task<T> WhenProductReady<T>(Func<Task<T>> call, int maxAttempts = 10)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await Run(call).ConfigureAwait(false);
            }
            catch (ProblemException ex) when (HasCode(ex, "PRODUCT_NOT_READY") && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt, 3))).ConfigureAwait(false);
            }
            catch (ProblemException ex) when (HasCode(ex, "PRODUCT_NOT_READY"))
            {
                throw new ProblemException(HttpStatusCode.Conflict,
                    "The bank's data is still being prepared by Plaid; try again in a moment.", ex);
            }
        }
    }
}
