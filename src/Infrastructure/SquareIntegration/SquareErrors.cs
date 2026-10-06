using System;
using System.Net;
using System.Text.Json;
using Square.Core.ErrorResponse;
using Square.Core.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// The single place SDK failures become <see cref="SquareIntegrationException"/>. Every operation in scope is
/// Case B (<c>ApiException&lt;RawError&gt;</c>), so the discriminators are the HTTP status and the first
/// <c>errors[].code</c> of Square's body.
/// </summary>
public static class SquareErrors
{
    public static bool IsStatus(ApiException<RawError> ex, HttpStatusCode status) => ex.StatusCode == status;

    public static bool IsNotFound(ApiException<RawError> ex) => ex.StatusCode == HttpStatusCode.NotFound;

    public static bool IsRateLimited(ApiException<RawError> ex) => (int)ex.StatusCode == 429;

    /// <summary>Reads the first Square error code from a raw error body; null when the body is not Square's JSON.</summary>
    public static string? ErrorCode(ApiException<RawError> ex)
    {
        try
        {
            using var document = JsonDocument.Parse(ex.Error.ReadAsString());
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String)
            {
                return code.GetString();
            }
        }
        catch (JsonException)
        {
            // A gateway can answer with HTML or plain text; the status still classifies it.
        }
        return null;
    }

    /// <summary>
    /// Converts any exception raised by an SDK call into the integration's failure type.
    /// Caller cancellation (<see cref="OperationCanceledException"/>) must not be passed here.
    /// </summary>
    public static SquareIntegrationException Translate(Exception exception, string operation, bool isWrite = false)
    {
        switch (exception)
        {
            case SquareIntegrationException own:
                return own;
            case AuthSchemeException auth when auth.InnerException is SquareIntegrationException inner:
                return inner;
            case AuthSchemeException auth:
                return new SquareIntegrationException(SquareFailureKind.AuthorizationFailed,
                    $"Square credentials could not be applied for {operation}.", innerException: auth);
            case ApiException<RawError> api:
                return FromStatus(api.StatusCode, ErrorCode(api), operation, api);
            case ResponseDeserializationException unreadable:
                // 2xx: the call may have succeeded but its result cannot be read. Otherwise only the detail was lost.
                if ((int)unreadable.StatusCode is >= 200 and < 300)
                {
                    return new SquareIntegrationException(
                        isWrite ? SquareFailureKind.OutcomeUnknown : SquareFailureKind.Unavailable,
                        $"Square returned a response to {operation} that could not be processed.",
                        unreadable.StatusCode, innerException: unreadable);
                }
                return FromStatus(unreadable.StatusCode, null, operation, unreadable);
            case SdkTimeoutException timeout:
                return new SquareIntegrationException(
                    isWrite ? SquareFailureKind.OutcomeUnknown : SquareFailureKind.Unavailable,
                    $"Square did not answer {operation} in time.", innerException: timeout);
            case SdkConnectionException connection:
                return new SquareIntegrationException(
                    isWrite ? SquareFailureKind.OutcomeUnknown : SquareFailureKind.Unavailable,
                    $"Square could not be reached for {operation}.", innerException: connection);
            default:
                return new SquareIntegrationException(SquareFailureKind.Unavailable,
                    $"{operation} failed unexpectedly.", innerException: exception);
        }
    }

    private static SquareIntegrationException FromStatus(HttpStatusCode status, string? code, string operation, Exception inner)
    {
        var numeric = (int)status;
        var kind = numeric switch
        {
            401 or 403 => SquareFailureKind.AuthorizationFailed,
            429 => SquareFailureKind.RateLimited,
            >= 400 and < 500 => SquareFailureKind.Rejected,
            _ => SquareFailureKind.Unavailable,
        };
        var message = kind switch
        {
            SquareFailureKind.AuthorizationFailed =>
                $"Square refused the shop's access for {operation}; reconnect the Square account.",
            SquareFailureKind.RateLimited => $"Square is rate limiting {operation}; try again shortly.",
            SquareFailureKind.Rejected => $"Square rejected {operation}" + (code is null ? "." : $" ({code})."),
            _ => $"Square is unavailable for {operation} (HTTP {numeric}).",
        };
        return new SquareIntegrationException(kind, message, status, code, inner);
    }
}
