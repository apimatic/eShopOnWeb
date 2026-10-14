using System;
using System.Collections.Generic;
using System.Net;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// One mapping from a Maxio failure to what the caller sees, shared by every call site. Typed
/// <c>ApiException&lt;{Operation}Error&gt;</c> bodies are read in each call site's own catch (where the concrete
/// error type is known); this class handles the status-only and transport leaves.
/// </summary>
internal static class MaxioFailures
{
    /// <summary>Maps a provider HTTP status (plus any safe validation messages) to a caller-facing failure.</summary>
    public static BillingException FromStatus(ILogger logger, string operation, HttpStatusCode status, IReadOnlyList<string>? providerErrors = null, Exception? inner = null)
    {
        var code = (int)status;
        switch (code)
        {
            case 401:
            case 403:
                // OUR credentials — the shopper did nothing wrong and cannot fix it.
                logger.LogError("Maxio {Operation} was refused with {StatusCode}: check Maxio:ApiKey and Maxio:Subdomain/BaseUrl.", operation, code);
                return new BillingException(StatusCodes.Status502BadGateway, "The billing provider is unavailable.", null, inner);
            case 429:
                logger.LogWarning("Maxio {Operation} was rate limited ({StatusCode}).", operation, code);
                return new BillingException(StatusCodes.Status503ServiceUnavailable, "The billing provider is busy. Please retry shortly.", null, inner);
            case 422:
                logger.LogWarning("Maxio {Operation} rejected the request ({StatusCode}): {ProviderErrors}", operation, code, providerErrors is null ? "" : string.Join("; ", providerErrors));
                return new BillingException(StatusCodes.Status422UnprocessableEntity, "The billing provider rejected the request.", providerErrors, inner);
            case >= 400 and < 500:
                // Every Maxio input is built by this service, so a provider 4xx is our defect or our configuration.
                logger.LogError("Maxio {Operation} failed with {StatusCode}.", operation, code);
                return new BillingException(StatusCodes.Status502BadGateway, "The billing provider could not process the request.", null, inner);
            default:
                logger.LogError("Maxio {Operation} failed with {StatusCode}.", operation, code);
                return new BillingException(StatusCodes.Status502BadGateway, "The billing provider is unavailable.", null, inner);
        }
    }

    /// <summary>
    /// The fallback arms of the catch ladder, for any SDK exception the call site did not handle with a typed
    /// catch: Case B <see cref="RawError"/>, unreadable bodies, transport failures, timeouts and credential errors.
    /// </summary>
    public static BillingException FromSdk(ILogger logger, string operation, SdkException exception) => exception switch
    {
        ApiException<RawError> raw => FromStatus(logger, operation, raw.StatusCode, null, raw),
        ResponseDeserializationException unreadable => Unreadable(logger, operation, unreadable),
        ApiException api => FromStatus(logger, operation, api.StatusCode, null, api),
        SdkTimeoutException timeout => Timeout(logger, operation, timeout),
        SdkConnectionException connection => Unreachable(logger, operation, connection),
        AuthSchemeException auth => CredentialsNotApplied(logger, operation, auth),
        _ => Unexpected(logger, operation, exception),
    };

    /// <summary>The request's whole-call budget elapsed (not a caller disconnect).</summary>
    public static BillingException BudgetExceeded(ILogger logger, string operation, Exception inner)
    {
        logger.LogWarning("Maxio {Operation} did not complete within the operation budget.", operation);
        return new BillingException(StatusCodes.Status504GatewayTimeout, "The billing provider did not respond in time. Please retry.", null, inner);
    }

    private static BillingException Unreadable(ILogger logger, string operation, ResponseDeserializationException ex)
    {
        // 2xx: the call may have worked and we cannot read the result. Non-2xx: refused, detail lost.
        logger.LogError(ex, "Maxio {Operation} returned a {StatusCode} body that could not be read as {TargetType}.", operation, (int)ex.StatusCode, ex.TargetType.Name);
        return (int)ex.StatusCode is >= 200 and < 300
            ? new BillingException(StatusCodes.Status502BadGateway, "The billing provider returned a response that could not be processed.", null, ex)
            : FromStatus(logger, operation, ex.StatusCode, null, ex);
    }

    private static BillingException Timeout(ILogger logger, string operation, SdkTimeoutException ex)
    {
        logger.LogWarning("Maxio {Operation} received no response within {Timeout}.", operation, ex.Timeout);
        return new BillingException(StatusCodes.Status504GatewayTimeout, "The billing provider did not respond in time. Please retry.", null, ex);
    }

    private static BillingException Unreachable(ILogger logger, string operation, SdkConnectionException ex)
    {
        logger.LogWarning(ex.InnerException, "Maxio {Operation} could not reach the provider.", operation);
        return new BillingException(StatusCodes.Status502BadGateway, "The billing provider is unreachable. Please retry.", null, ex);
    }

    private static BillingException CredentialsNotApplied(ILogger logger, string operation, AuthSchemeException ex)
    {
        logger.LogError(ex.InnerException, "Maxio {Operation}: credentials could not be applied.", operation);
        return new BillingException(StatusCodes.Status502BadGateway, "The billing provider is unavailable.", null, ex);
    }

    private static BillingException Unexpected(ILogger logger, string operation, SdkException ex)
    {
        logger.LogError(ex, "Maxio {Operation} failed unexpectedly.", operation);
        return new BillingException(StatusCodes.Status502BadGateway, "The billing provider is unavailable.", null, ex);
    }
}
