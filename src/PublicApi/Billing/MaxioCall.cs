using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Runs one Maxio SDK call and turns every failure the SDK can raise into a <see cref="BillingException"/>
/// carrying a status and a reason.
/// </summary>
public class MaxioCall
{
    private readonly ILogger<MaxioCall> _logger;

    public MaxioCall(ILogger<MaxioCall> logger)
    {
        _logger = logger;
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            // Covers ApiException<TError> and ResponseDeserializationException (the server answered).
            var reason = Describe(ex);
            _logger.LogWarning("Maxio call failed: {Call} -> {Status} {Reason}", ex.Message, (int)ex.StatusCode, reason);
            throw Translate(ex.StatusCode, reason);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning(ex, "Maxio call timed out");
            throw new BillingException(HttpStatusCode.GatewayTimeout, "The billing system did not answer in time.");
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning(ex, "Maxio call could not complete");
            throw new BillingException(HttpStatusCode.BadGateway, "The billing system could not be reached.");
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials are not usable");
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Billing is not configured: the Maxio credentials are not usable.");
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Maxio call failed");
            throw new BillingException(HttpStatusCode.BadGateway, "The billing system call failed.");
        }
    }

    public async Task RunAsync(Func<Task> call) =>
        await RunAsync(async () =>
        {
            await call();
            return true;
        });

    /// <summary>True when the call failed because Maxio answered 404.</summary>
    public static bool IsNotFound(BillingException ex) => ex.StatusCode == HttpStatusCode.NotFound;

    private static BillingException Translate(HttpStatusCode status, string reason) => (int)status switch
    {
        404 => new BillingException(HttpStatusCode.NotFound, $"Not found in the billing system. {reason}".Trim()),
        422 or 400 => new BillingException(HttpStatusCode.UnprocessableEntity, $"The billing system refused the request. {reason}".Trim()),
        401 or 403 => new BillingException(HttpStatusCode.ServiceUnavailable, "The billing system rejected the app's credentials."),
        429 => new BillingException(HttpStatusCode.ServiceUnavailable, "The billing system is rate limiting requests; try again shortly."),
        _ => new BillingException(HttpStatusCode.BadGateway, $"The billing system failed ({(int)status}).")
    };

    private static string Describe(ApiException ex)
    {
        try
        {
            var text = ex switch
            {
                ApiException<RawError> e => e.Error.ReadAsString(),
                ApiException<CreateSubscriptionError> e => Typed(e.Error, e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null),
                ApiException<UpdateSubscriptionError> e => Typed(e.Error, e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null),
                ApiException<PauseSubscriptionError> e => Typed(e.Error, e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null),
                ApiException<ResumeSubscriptionError> e => Typed(e.Error, e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null),
                ApiException<ReactivateSubscriptionError> e => Typed(e.Error, e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null),
                ApiException<MigrateSubscriptionProductError> e => Typed(e.Error, e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null),
                ApiException<CreateProductError> e => Typed(e.Error, e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null),
                ApiException<CreateProductFamilyError> e => Typed(e.Error, e.Error.TryGetErrorListResponse1(out var v) ? v.Errors : null),
                ApiException<CancelSubscriptionError> e => Typed(e.Error, null),
                ApiException<CreateProductCurrencyPricesError> e => Typed(e.Error,
                    e.Error.TryGetErrorArrayMapResponse1(out var v) && v.Errors != null
                        ? v.Errors.Select(kv => $"{kv.Key}: {kv.Value}").ToList()
                        : null),
                _ => string.Empty
            };
            return text;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string Typed(ApiError error, IReadOnlyList<string>? messages)
    {
        if (messages is { Count: > 0 }) return string.Join(" ", messages.Distinct());
        return error.TryGetRawError(out var raw) ? raw.ReadAsString() : string.Empty;
    }
}
