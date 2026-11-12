using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The one place where Maxio SDK failures become <see cref="BillingException"/>s: one ladder for every call.
/// A write whose connection fails (or whose 2xx body is unreadable) may already have landed, so it is settled
/// by re-reading Maxio before anything is reported.
/// </summary>
public class MaxioGateway
{
    private readonly ILogger<MaxioGateway> _logger;

    public MaxioGateway(MaxioAdvancedBillingClient client, ILogger<MaxioGateway> logger)
    {
        Client = client;
        _logger = logger;
    }

    public MaxioAdvancedBillingClient Client { get; }

    /// <summary>A read, or a write that cannot be observed afterwards.</summary>
    public Task<T> ReadAsync<T>(Func<MaxioAdvancedBillingClient, Task<T>> call) where T : class => RunAsync(call, null);

    /// <summary>
    /// A write. <paramref name="settle"/> re-reads Maxio after an unknown outcome and returns the result when the
    /// write is confirmed, or null when it cannot be confirmed.
    /// </summary>
    public Task<T> WriteAsync<T>(Func<MaxioAdvancedBillingClient, Task<T>> call, Func<Task<T?>> settle) where T : class =>
        RunAsync(call, settle);

    private async Task<T> RunAsync<T>(Func<MaxioAdvancedBillingClient, Task<T>> call, Func<Task<T?>>? settle) where T : class
    {
        try
        {
            return await call(Client);
        }
        catch (ApiException<PauseSubscriptionError> e) { throw Map(e, ListMessage(e.Error.TryGetErrorListResponse1(out var x) ? x.Errors : null, e.Error)); }
        catch (ApiException<ResumeSubscriptionError> e) { throw Map(e, ListMessage(e.Error.TryGetErrorListResponse1(out var x) ? x.Errors : null, e.Error)); }
        catch (ApiException<ReactivateSubscriptionError> e) { throw Map(e, ListMessage(e.Error.TryGetErrorListResponse1(out var x) ? x.Errors : null, e.Error)); }
        catch (ApiException<MigrateSubscriptionProductError> e) { throw Map(e, ListMessage(e.Error.TryGetErrorListResponse1(out var x) ? x.Errors : null, e.Error)); }
        catch (ApiException<UpdateSubscriptionError> e) { throw Map(e, ListMessage(e.Error.TryGetErrorListResponse1(out var x) ? x.Errors : null, e.Error)); }
        catch (ApiException<CreateSubscriptionError> e) { throw Map(e, ListMessage(e.Error.TryGetErrorListResponse1(out var x) ? x.Errors : null, e.Error)); }
        catch (ApiException<CreateProductError> e) { throw Map(e, ListMessage(e.Error.TryGetErrorListResponse1(out var x) ? x.Errors : null, e.Error)); }
        catch (ApiException<CreateProductFamilyError> e) { throw Map(e, ListMessage(e.Error.TryGetErrorListResponse1(out var x) ? x.Errors : null, e.Error)); }
        catch (ApiException<CreateProductCurrencyPricesError> e) { throw Map(e, MapMessage(e.Error.TryGetErrorArrayMapResponse1(out var x) ? x.Errors : null, e.Error)); }
        catch (ApiException<UpdateProductCurrencyPricesError> e) { throw Map(e, MapMessage(e.Error.TryGetErrorArrayMapResponse1(out var x) ? x.Errors : null, e.Error)); }
        catch (ApiException<CreateCustomerError> e) { throw Map(e, RawMessage(e.Error) ?? "Customer was refused."); }
        catch (ApiException<CancelSubscriptionError> e) { throw Map(e, RawMessage(e.Error) ?? "Cancellation was refused."); }
        catch (ApiException<FindSubscriptionError> e) { throw Map(e, RawMessage(e.Error) ?? "Subscription was not found."); }
        catch (ApiException<RawError> e) { throw Map(e, TrimBody(e.Error.ReadAsString())); }
        catch (Exception ex) when (IsUnknownOutcome(ex))
        {
            _logger.LogWarning(ex, "Maxio call outcome unknown; re-reading to settle it");
            if (settle != null)
            {
                T? settled = null;
                try { settled = await settle(); }
                catch (Exception reread) { _logger.LogWarning(reread, "Re-read after unknown outcome failed"); }
                if (settled != null) return settled;
            }
            throw new BillingException(504, "Billing provider did not answer in time. The outcome is unknown; check the subscription before retrying.", ex);
        }
        catch (ResponseDeserializationException ex)
        {
            // An unreadable error body: the provider rejected the request, only the detail was lost.
            throw Map(ex, "Billing provider rejected the request.");
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials are missing or were rejected");
            throw new BillingException(502, "Billing provider is unavailable.", ex);
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Unexpected Maxio SDK failure");
            throw new BillingException(502, "Billing provider is unavailable.", ex);
        }
    }

    private static bool IsUnknownOutcome(Exception ex) =>
        ex is SdkConnectionException ||
        ex is ResponseDeserializationException { StatusCode: >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices };

    /// <summary>One failure-kind to status mapping for every operation.</summary>
    private BillingException Map(ApiException ex, string? reason)
    {
        var status = (int)ex.StatusCode;
        _logger.LogWarning("Maxio answered {Status} for {Method} {Uri}: {Reason}", status, ex.Method, ex.RequestUri, reason);
        var text = string.IsNullOrWhiteSpace(reason) ? "no reason given" : reason;
        return status switch
        {
            401 or 403 => new BillingException(502, "Billing provider is unavailable.", ex),
            429 => new BillingException(503, "Billing provider is busy, try again shortly.", ex),
            404 => new BillingException(404, $"Billing provider could not find it: {text}", ex),
            >= 400 and < 500 => new BillingException(status, $"Billing provider refused the request: {text}", ex),
            _ => new BillingException(502, "Billing provider failed to process the request.", ex),
        };
    }

    private static string? ListMessage(IReadOnlyList<string>? errors, ApiError error) =>
        errors is { Count: > 0 } ? string.Join("; ", errors) : RawMessage(error);

    private static string? MapMessage(IReadOnlyDictionary<string, object>? errors, ApiError error) =>
        errors is { Count: > 0 } ? string.Join("; ", errors.Select(kv => $"{kv.Key}: {kv.Value}")) : RawMessage(error);

    private static string? RawMessage(ApiError error) =>
        error.TryGetRawError(out var raw) ? TrimBody(raw.ReadAsString()) : null;

    private static string TrimBody(string body) => body.Length > 300 ? body[..300] : body;
}
