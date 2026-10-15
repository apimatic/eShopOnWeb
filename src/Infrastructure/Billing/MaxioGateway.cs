using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class ProviderFault(HttpStatusCode? statusCode, bool unknown) : Exception("Billing provider unavailable.")
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public bool Unknown { get; } = unknown;
}

// All provider contracts come from the installed plugin's SDK and contract sheet.
public sealed class MaxioGateway(MaxioAdvancedBillingClient client, ILogger<MaxioGateway> logger)
{
    public Task<IReadOnlyList<ProductResponse>> ProductsAsync(string family, int page, CancellationToken ct) => Execute(
        () => client.ProductFamilies.ListProductsForProductFamily(new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
        { ProductFamilyId = "handle:" + family, Page = page, PerPage = 200, IncludeArchived = false }, cancellationToken: ct));

    public async Task<Customer?> CustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var result = await Execute(() => client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct));
            return ValidateCustomer(result.Customer, reference);
        }
        catch (ProviderFault ex) when (ex.StatusCode == HttpStatusCode.NotFound && !ex.Unknown) { return null; }
    }

    public async Task<Customer> CreateCustomerAsync(string reference, string email, CancellationToken ct)
    {
        var result = await Execute(() => client.Customers.CreateCustomer(
            new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = email.Split('@')[0], LastName = "Shopper", Email = email, Reference = reference
                    }
                }
            }, cancellationToken: ct));
        return ValidateCustomer(result.Customer, reference);
    }

    public async Task<Subscription?> SubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var result = await Execute(() => client.Subscriptions.FindSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference }, cancellationToken: ct));
            if (result.Subscription == null || result.Subscription.Reference != reference) throw new ProviderFault(null, true);
            return result.Subscription;
        }
        catch (ProviderFault ex) when (ex.StatusCode == HttpStatusCode.NotFound && !ex.Unknown) { return null; }
    }

    public async Task<Subscription> CreateSubscriptionAsync(string reference, int customerId, string handle, CancellationToken ct)
    {
        var result = await Execute(() => client.Subscriptions.CreateSubscription(
            new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        Reference = reference, CustomerId = customerId, ProductHandle = handle,
                        PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                    }
                }
            }, cancellationToken: ct));
        if (result.Subscription == null || result.Subscription.Reference != reference) throw new ProviderFault(null, true);
        return result.Subscription;
    }

    public Task<IReadOnlyList<SubscriptionResponse>> SubscriptionsAsync(int customerId, int page, CancellationToken ct) => Execute(
        () => client.Subscriptions.ListSubscriptions(new MaxioAdvancedBilling.Requests.Subscriptions.ListSubscriptionsRequest
        { CustomerId = customerId, Page = page, PerPage = 200 }, cancellationToken: ct));

    private static Customer ValidateCustomer(Customer? customer, string reference)
    {
        if (customer?.Id is not > 0 || customer.Reference != reference) throw new ProviderFault(null, true);
        return customer;
    }

    private async Task<T> Execute<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                if (body.Errors?.TryGetCustomerError(out _) == true) logger.LogWarning("Maxio rejected customer fields.");
                else if (body.Errors?.TryGetListOfString(out _) == true) logger.LogWarning("Maxio rejected customer creation.");
            }
            else if (ex.Error.TryGetRawError(out var raw)) LogStatus(raw.StatusCode);
            throw Fault(ex.StatusCode);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out _)) logger.LogWarning("Maxio rejected subscription creation.");
            else if (ex.Error.TryGetRawError(out var raw)) LogStatus(raw.StatusCode);
            throw Fault(ex.StatusCode);
        }
        catch (ApiException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _)) logger.LogWarning("Configured Maxio product family was not found.");
            else if (ex.Error.TryGetRawError(out var raw)) LogStatus(raw.StatusCode);
            throw Fault(ex.StatusCode);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out var missing)) LogStatus(missing.StatusCode);
            else if (ex.Error.TryGetRawError(out var raw)) LogStatus(raw.StatusCode);
            throw Fault(ex.StatusCode);
        }
        catch (ApiException<RawError> ex) { LogStatus(ex.Error.StatusCode); throw Fault(ex.StatusCode); }
        catch (ResponseDeserializationException ex)
        {
            logger.LogWarning("Maxio response did not match its SDK contract; HTTP {Status}.", (int)ex.StatusCode);
            throw new ProviderFault(ex.StatusCode, (int)ex.StatusCode is >= 200 and < 300 or >= 500);
        }
        catch (ApiException ex) { throw Fault(ex.StatusCode); }
        catch (SdkConnectionException) { logger.LogWarning("Maxio connection failed or timed out."); throw new ProviderFault(null, true); }
        catch (SdkException) { logger.LogError("Maxio SDK could not complete the operation."); throw new ProviderFault(null, false); }
    }

    private void LogStatus(HttpStatusCode status) => logger.LogDebug("Maxio returned HTTP {Status}.", (int)status);
    private static ProviderFault Fault(HttpStatusCode status) => new(status, (int)status >= 500 || (int)status is >= 200 and < 300);
}
