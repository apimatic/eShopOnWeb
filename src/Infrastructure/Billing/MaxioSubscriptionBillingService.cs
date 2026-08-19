using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IConfiguration configuration,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _productFamilyHandle = configuration[$"{MaxioOptions.SectionName}:ProductFamilyHandle"] ?? string.Empty;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _productFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new BillingException((int)HttpStatusCode.InternalServerError, "Billing is not configured.");
        }

        var plans = new List<SubscriptionPlan>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            IReadOnlyList<ProductResponse> responses;
            try
            {
                responses = await Bounded(ct => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + familyHandle,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: page,
                    perPage: perPage,
                    ct: ct), cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw MapListProductsError(ex);
            }
            catch (Exception ex) when (IsTransport(ex))
            {
                throw Unavailable("Unable to list subscription plans.", ex);
            }
            catch (JsonException ex)
            {
                throw Unreadable("The billing provider returned a response that could not be processed.", ex);
            }

            if (responses.Count == 0)
            {
                break;
            }

            foreach (var response in responses)
            {
                var product = response.Product;
                if (product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(ToPlan(product));
            }

            if (responses.Count < perPage)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<CustomerSubscription> SubscribeAsync(BillingBuyer buyer, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException((int)HttpStatusCode.BadRequest, "A product handle is required.");
        }

        var customer = await EnsureCustomerAsync(buyer, cancellationToken);
        var reference = $"{buyer.Id}:{productHandle}";

        var existing = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        SubscriptionResponse created;
        try
        {
            created = await WriteOnce(ct => _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customer.Id,
                        Reference = reference,
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                ct: ct), cancellationToken);
        }
        catch (DuplicateOutboundPostException ex)
        {
            return await RequireFoundAfterWrite(reference, "The subscription request may already have been processed.", ex, cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var found = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (found is not null)
            {
                return found;
            }

            throw MapCreateSubscriptionError(ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            return await RequireFoundAfterWrite(reference, "Unable to create the subscription.", ex, cancellationToken);
        }
        catch (JsonException ex)
        {
            var found = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (found is not null)
            {
                return found;
            }

            throw Unreadable("The billing provider returned a response that could not be processed.", ex);
        }

        var subscription = created.Subscription;
        if (subscription is null)
        {
            var found = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (found is not null)
            {
                return found;
            }

            throw Unreadable("The billing provider returned an empty subscription.", null);
        }

        return ToCustomerSubscription(subscription);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(BillingBuyer buyer, CancellationToken cancellationToken)
    {
        var customer = await TryReadCustomerByReferenceAsync(buyer.Id, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await Bounded(ct => _client.Customers.ListCustomerSubscriptions(
                customerId: customer.Id.Value,
                ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "Unable to list subscriptions.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unavailable("Unable to list subscriptions.", ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable("The billing provider returned a response that could not be processed.", ex);
        }

        var result = new List<CustomerSubscription>(responses.Count);
        foreach (var response in responses)
        {
            if (response.Subscription is not null)
            {
                result.Add(ToCustomerSubscription(response.Subscription));
            }
        }

        return result;
    }

    private async Task<Customer> EnsureCustomerAsync(BillingBuyer buyer, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerByReferenceAsync(buyer.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        CustomerResponse created;
        try
        {
            created = await WriteOnce(ct => _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = buyer.FirstName,
                        LastName = buyer.LastName,
                        Email = buyer.Email,
                        Reference = buyer.Id
                    }
                },
                ct: ct), cancellationToken);
        }
        catch (DuplicateOutboundPostException ex)
        {
            return await RequireCustomerAfterWrite(buyer.Id, "The customer request may already have been processed.", ex, cancellationToken);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var raced = await TryReadCustomerByReferenceAsync(buyer.Id, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            var byEmail = await TryFindCustomerByEmailAsync(buyer.Email, cancellationToken);
            if (byEmail is not null)
            {
                return byEmail;
            }

            throw MapCreateCustomerError(ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            return await RequireCustomerAfterWrite(buyer.Id, "Unable to create the billing customer.", ex, cancellationToken);
        }
        catch (JsonException ex)
        {
            var raced = await TryReadCustomerByReferenceAsync(buyer.Id, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw Unreadable("The billing provider returned a response that could not be processed.", ex);
        }

        if (created.Customer.Id is null)
        {
            var reread = await TryReadCustomerByReferenceAsync(buyer.Id, cancellationToken);
            if (reread is not null)
            {
                return reread;
            }

            throw Unreadable("The billing provider returned an empty customer.", null);
        }

        return created.Customer;
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Customers.ReadCustomerByReference(
                reference: reference,
                ct: ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw MapRaw(ex.Error, "Unable to look up the billing customer.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unavailable("Unable to look up the billing customer.", ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable("The billing provider returned a response that could not be processed.", ex);
        }
    }

    private async Task<Customer?> TryFindCustomerByEmailAsync(string email, CancellationToken cancellationToken)
    {
        IReadOnlyList<CustomerResponse> matches;
        try
        {
            matches = await Bounded(ct => _client.Customers.ListCustomers(
                direction: null,
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                q: email,
                page: 1,
                perPage: 50,
                ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "Unable to look up the billing customer.");
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unavailable("Unable to look up the billing customer.", ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable("The billing provider returned a response that could not be processed.", ex);
        }

        foreach (var response in matches)
        {
            if (string.Equals(response.Customer.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                return response.Customer;
            }
        }

        return null;
    }

    private async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Subscriptions.FindSubscription(
                reference: reference,
                ct: ct), cancellationToken);
            return response.Subscription is null ? null : ToCustomerSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw MapRaw(raw, "Unable to look up the subscription.");
            }

            throw new BillingException((int)HttpStatusCode.BadGateway, "Unable to look up the subscription.", ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw Unavailable("Unable to look up the subscription.", ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable("The billing provider returned a response that could not be processed.", ex);
        }
    }

    private async Task<CustomerSubscription> RequireFoundAfterWrite(
        string reference,
        string failureMessage,
        Exception inner,
        CancellationToken cancellationToken)
    {
        var found = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (found is not null)
        {
            return found;
        }

        throw new BillingException((int)HttpStatusCode.BadGateway, failureMessage, inner);
    }

    private async Task<Customer> RequireCustomerAfterWrite(
        string reference,
        string failureMessage,
        Exception inner,
        CancellationToken cancellationToken)
    {
        var found = await TryReadCustomerByReferenceAsync(reference, cancellationToken);
        if (found is not null)
        {
            return found;
        }

        throw new BillingException((int)HttpStatusCode.BadGateway, failureMessage, inner);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task<T> WriteOnce<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        PreventRetryPostHandler.BeginWrite();
        return await Bounded(call, cancellationToken);
    }

    private static SubscriptionPlan ToPlan(Product product)
    {
        return new SubscriptionPlan(
            Handle: product.Handle!,
            Name: product.Name ?? product.Handle!,
            Description: product.Description,
            Price: ToMoney(product.PriceInCents),
            Interval: product.Interval ?? 1,
            IntervalUnit: product.IntervalUnit?.Value ?? "month");
    }

    private static CustomerSubscription ToCustomerSubscription(Subscription subscription)
    {
        if (subscription.Id is null)
        {
            throw Unreadable("The billing provider returned a subscription without an id.", null);
        }

        return new CustomerSubscription(
            Id: subscription.Id.Value,
            ProductHandle: subscription.Product?.Handle,
            ProductName: subscription.Product?.Name,
            Price: subscription.ProductPriceInCents is null ? null : ToMoney(subscription.ProductPriceInCents),
            State: subscription.State?.Value ?? "unknown",
            NextBillingAt: subscription.NextAssessmentAt);
    }

    private static decimal ToMoney(long? cents) => (cents ?? 0L) / 100m;

    private BillingException MapListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message) && !string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("Maxio list products for family failed: {Message}", message);
            return new BillingException((int)HttpStatusCode.NotFound, "Subscription plans are not available.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "Unable to list subscription plans.");
        }

        return new BillingException((int)HttpStatusCode.BadGateway, "Unable to list subscription plans.", ex);
    }

    private BillingException MapCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingException((int)HttpStatusCode.UnprocessableEntity, "Unable to create the billing customer.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "Unable to create the billing customer.");
        }

        return new BillingException((int)HttpStatusCode.BadGateway, "Unable to create the billing customer.", ex);
    }

    private BillingException MapCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list) && list.Errors is { Count: > 0 })
        {
            return new BillingException((int)HttpStatusCode.UnprocessableEntity, string.Join(" ", list.Errors), ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "Unable to create the subscription.");
        }

        return new BillingException((int)HttpStatusCode.BadGateway, "Unable to create the subscription.", ex);
    }

    private BillingException MapRaw(RawError raw, string fallback)
    {
        var status = (int)raw.StatusCode;
        if (status >= 400 && status < 500)
        {
            return new BillingException(status, fallback);
        }

        _logger.LogWarning("Maxio returned HTTP {Status}", status);
        return new BillingException((int)HttpStatusCode.BadGateway, fallback);
    }

    private static bool IsTransport(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or DuplicateOutboundPostException;

    private static BillingException Unavailable(string message, Exception inner) =>
        new((int)HttpStatusCode.BadGateway, message, inner);

    private static BillingException Unreadable(string message, Exception? inner) =>
        new((int)HttpStatusCode.BadGateway, message, inner);
}
