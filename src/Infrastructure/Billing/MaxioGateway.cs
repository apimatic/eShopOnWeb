using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

// This boundary retains provider status and ambiguous-write information; no provider body reaches the caller.
public sealed class MaxioFailure : Exception
{
    public int? StatusCode { get; }
    public bool IsAmbiguous { get; }
    public bool IsConfirmedMissing { get; }
    public MaxioFailure(int? status, bool ambiguous, bool confirmedMissing = false) : base("The billing provider could not complete this request.")
    { StatusCode = status; IsAmbiguous = ambiguous; IsConfirmedMissing = confirmedMissing; }
}

public sealed class MaxioGateway
{
    private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioGateway> _logger;
    public MaxioGateway(MaxioAdvancedBilling.MaxioAdvancedBillingClient client, ILogger<MaxioGateway> logger)
    { _client = client; _logger = logger; }

    public Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ProductsAsync(string family, int page, CancellationToken ct) =>
        Call(() => _client.ProductFamilies.ListProductsForProductFamily(
            new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
            { ProductFamilyId = "handle:" + family, Page = page, PerPage = 200, IncludeArchived = false }, cancellationToken: ct));

    public async Task<MaxioAdvancedBilling.Models.Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            return (await Call(() => _client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct))).Customer
                ?? throw new MaxioFailure(null, false);
        }
        catch (MaxioFailure ex) when (ex.IsConfirmedMissing) { return null; }
    }

    public async Task<MaxioAdvancedBilling.Models.Customer> CreateCustomerAsync(string reference, string email, CancellationToken ct) =>
        (await Call(() => _client.Customers.CreateCustomer(
            new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
            {
                Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                {
                    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                    { FirstName = "eShop", LastName = "Shopper", Email = email, Reference = reference }
                }
            }, cancellationToken: ct))).Customer ?? throw new MaxioFailure(null, true);

    public async Task<MaxioAdvancedBilling.Models.Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            return (await Call(() => _client.Subscriptions.FindSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference }, cancellationToken: ct))).Subscription
                ?? throw new MaxioFailure(null, false);
        }
        catch (MaxioFailure ex) when (ex.IsConfirmedMissing) { return null; }
    }

    public async Task<MaxioAdvancedBilling.Models.Subscription> CreateSubscriptionAsync(string reference, int customerId, string handle, CancellationToken ct) =>
        (await Call(() => _client.Subscriptions.CreateSubscription(
            new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
            {
                Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                    { Reference = reference, CustomerId = customerId, ProductHandle = handle,
                        PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance }
                }
            }, cancellationToken: ct))).Subscription ?? throw new MaxioFailure(null, true);

    public Task<IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>> CustomerSubscriptionsAsync(int customerId, CancellationToken ct) =>
        Call(() => _client.Customers.ListCustomerSubscriptions(
            new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct));

    private async Task<T> Call<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _)) _logger.LogDebug("Configured product family was not found.");
            else if (ex.Error.TryGetRawError(out _)) _logger.LogDebug("Catalog returned an untyped error.");
            throw Failure(ex);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _)) _logger.LogDebug("Customer validation was rejected.");
            else if (ex.Error.TryGetRawError(out _)) _logger.LogDebug("Customer creation returned an untyped error.");
            throw Failure(ex);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out _)) _logger.LogDebug("Subscription validation was rejected.");
            else if (ex.Error.TryGetRawError(out _)) _logger.LogDebug("Subscription creation returned an untyped error.");
            throw Failure(ex);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _)) _logger.LogDebug("Subscription reference was not found.");
            else if (ex.Error.TryGetRawError(out _)) _logger.LogDebug("Subscription lookup returned an untyped error.");
            throw Failure(ex);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> ex)
        { _ = ex.Error.StatusCode; throw Failure(ex); }
        catch (MaxioAdvancedBilling.Core.Exceptions.ResponseDeserializationException ex) { throw Failure(ex, false); }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException ex) { throw Failure(ex); }
        catch (MaxioAdvancedBilling.Core.Exceptions.SdkException)
        { _logger.LogWarning("Billing transport or authentication failure. Trace {TraceId}", System.Diagnostics.Activity.Current?.TraceId); throw new MaxioFailure(null, true); }
    }

    private MaxioFailure Failure(MaxioAdvancedBilling.Core.Exceptions.ApiException ex, bool parsed = true)
    {
        var status = (int)ex.StatusCode;
        // Typed payloads in this scope have no correlation field. Read only the response metadata.
        var requestId = ex.Headers.TryGetValues("X-Request-Id", out var ids) ? string.Join(",", ids) : null;
        _logger.LogWarning("Billing provider HTTP {Status}; request {ProviderRequestId}; trace {TraceId}",
            status, requestId, System.Diagnostics.Activity.Current?.TraceId);
        return new MaxioFailure(status, status < 400 || status >= 500 || status == (int)HttpStatusCode.RequestTimeout,
            parsed && status == 404);
    }

    public static BillingException PublicError(MaxioFailure ex) => ex.StatusCode switch
    {
        400 or 409 or 422 => new BillingException(ex.StatusCode.Value, "The billing provider rejected this subscription request."),
        404 => new BillingException(404, "The requested billing resource is unavailable."),
        429 => new BillingException(503, "Billing is temporarily busy. Try again later."),
        _ => new BillingException(502, "Billing is temporarily unavailable.")
    };
}
