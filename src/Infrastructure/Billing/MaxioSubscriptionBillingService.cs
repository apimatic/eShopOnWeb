using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
        => ExecuteAsync(() => ListPlansCoreAsync(cancellationToken), "Failed to list subscription plans.");

    public Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken)
        => ExecuteAsync(() => SubscribeCoreAsync(shopper, productHandle, cancellationToken), "Failed to create the subscription.");

    public Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken)
        => ExecuteAsync(() => ListMySubscriptionsCoreAsync(userId, cancellationToken), "Failed to list subscriptions.");

    private async Task<IReadOnlyList<SubscriptionPlan>> ListPlansCoreAsync(CancellationToken ct)
    {
        EnsureConfigured();

        var familyHandle = _settings.ProductFamilyHandle.Trim();
        var families = await _client.ProductFamilies.ListProductFamilies(
            dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct);

        var family = families
            .Select(item => item.ProductFamily)
            .FirstOrDefault(item => item?.Handle is string handle
                && string.Equals(handle, familyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is null)
        {
            throw new BillingProviderException("The configured product family was not found.", (int)HttpStatusCode.NotFound);
        }

        var products = new List<Product>();
        const int perPage = 20;
        var page = 1;

        while (true)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: family.Id.Value.ToString(),
                    dateField: null, filter: null, startDate: null, endDate: null,
                    startDatetime: null, endDatetime: null, includeArchived: false, include: null,
                    page: page, perPage: perPage, ct: ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new BillingProviderException("The configured product family was not found.", (int)HttpStatusCode.NotFound, ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MapRawError(raw, "Failed to list subscription plans.");
                }

                throw new BillingProviderException("Failed to list subscription plans.", (int)HttpStatusCode.BadGateway, ex);
            }

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var item in batch)
            {
                if (item.Product is { } product
                    && product.ArchivedAt is null
                    && !string.IsNullOrWhiteSpace(product.Handle))
                {
                    products.Add(product);
                }
            }

            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products
            .Select(MapPlan)
            .ToList();
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(ShopperIdentity shopper, string productHandle, CancellationToken ct)
    {
        EnsureConfigured();
        GuardShopper(shopper);

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingProviderException("A productHandle is required.", (int)HttpStatusCode.BadRequest);
        }

        productHandle = productHandle.Trim();

        var plans = await ListPlansCoreAsync(ct);
        if (!plans.Any(plan => string.Equals(plan.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingProviderException($"Unknown subscription plan '{productHandle}'.", (int)HttpStatusCode.BadRequest);
        }

        var customer = await EnsureCustomerAsync(shopper, ct);
        if (customer.Id is null)
        {
            throw new BillingProviderException("The billing provider returned a customer without an id.", (int)HttpStatusCode.BadGateway);
        }

        var customerId = customer.Id.Value;
        var preferredReference = $"{shopper.UserId}:{productHandle}";

        var existingByRef = await TryFindSubscriptionAsync(preferredReference, ct);
        if (existingByRef is not null && IsLive(existingByRef.State))
        {
            return new SubscribeResult(MapSubscription(existingByRef), Created: false);
        }

        var existingForCustomer = await ListCustomerSubscriptionsCoreAsync(customerId, ct);
        var liveSamePlan = existingForCustomer.FirstOrDefault(subscription =>
            subscription.Product?.Handle is string handle
            && string.Equals(handle, productHandle, StringComparison.OrdinalIgnoreCase)
            && IsLive(subscription.State));

        if (liveSamePlan is not null)
        {
            return new SubscribeResult(MapSubscription(liveSamePlan), Created: false);
        }

        var createReference = existingByRef is null
            ? preferredReference
            : $"{preferredReference}:{Guid.NewGuid():N}";

        try
        {
            var created = await _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customerId,
                        CustomerReference = shopper.UserId,
                        Reference = createReference,
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                ct: ct);

            if (created.Subscription is null)
            {
                throw new BillingProviderException("The billing provider did not return a subscription.", (int)HttpStatusCode.BadGateway);
            }

            return new SubscribeResult(MapSubscription(created.Subscription), Created: true);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var list))
            {
                var recovered = await TryFindSubscriptionAsync(createReference, ct)
                    ?? await TryFindSubscriptionAsync(preferredReference, ct);
                if (recovered is not null)
                {
                    return new SubscribeResult(MapSubscription(recovered), Created: false);
                }

                var message = list.Errors is { Count: > 0 }
                    ? string.Join(" ", list.Errors.Where(error => !string.IsNullOrWhiteSpace(error)))
                    : "The billing provider rejected the subscription.";
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = "The billing provider rejected the subscription.";
                }

                throw new BillingProviderException(message, (int)HttpStatusCode.UnprocessableEntity, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    var recovered = await TryFindSubscriptionAsync(createReference, ct)
                        ?? await TryFindSubscriptionAsync(preferredReference, ct);
                    if (recovered is not null)
                    {
                        return new SubscribeResult(MapSubscription(recovered), Created: false);
                    }
                }

                throw MapRawError(raw, "Failed to create the subscription.");
            }

            throw new BillingProviderException("Failed to create the subscription.", (int)HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            var status = MaxioStatusCaptureHandler.LastStatus.Value;
            if (status == HttpStatusCode.UnprocessableEntity)
            {
                var recovered = await TryFindSubscriptionAsync(createReference, ct)
                    ?? await TryFindSubscriptionAsync(preferredReference, ct);
                if (recovered is not null)
                {
                    return new SubscribeResult(MapSubscription(recovered), Created: false);
                }

                throw new BillingProviderException("The billing provider rejected the subscription.", (int)HttpStatusCode.UnprocessableEntity, ex);
            }

            throw MapJsonException(ex, "Failed to create the subscription.");
        }
    }

    private async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsCoreAsync(string userId, CancellationToken ct)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new BillingProviderException("A user id is required.", (int)HttpStatusCode.BadRequest);
        }

        var customer = await TryReadCustomerByReferenceAsync(userId, ct);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsCoreAsync(customer.Id.Value, ct);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<Customer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(shopper.UserId, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = shopper.FirstName,
                        LastName = shopper.LastName,
                        Email = shopper.Email,
                        Reference = shopper.UserId
                    }
                },
                ct: ct);

            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var raced = await TryReadCustomerByReferenceAsync(shopper.UserId, ct);
                if (raced is not null)
                {
                    return raced;
                }

                throw new BillingProviderException("The billing provider rejected the customer record.", (int)HttpStatusCode.UnprocessableEntity, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    var raced = await TryReadCustomerByReferenceAsync(shopper.UserId, ct);
                    if (raced is not null)
                    {
                        return raced;
                    }
                }

                throw MapRawError(raw, "Failed to create the billing customer.");
            }

            throw new BillingProviderException("Failed to create the billing customer.", (int)HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            var status = MaxioStatusCaptureHandler.LastStatus.Value;
            if (status == HttpStatusCode.UnprocessableEntity)
            {
                var raced = await TryReadCustomerByReferenceAsync(shopper.UserId, ct);
                if (raced is not null)
                {
                    return raced;
                }

                throw new BillingProviderException("The billing provider rejected the customer record.", (int)HttpStatusCode.UnprocessableEntity, ex);
            }

            throw MapJsonException(ex, "Failed to create the billing customer.");
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "Failed to look up the billing customer.");
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference: reference, ct: ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out RawError _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw MapRawError(raw, "Failed to look up the subscription.");
            }

            throw new BillingProviderException("Failed to look up the subscription.", (int)HttpStatusCode.BadGateway, ex);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsCoreAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
            return response
                .Select(item => item.Subscription)
                .Where(subscription => subscription is not null)
                .Select(subscription => subscription!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "Failed to list subscriptions.");
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string fallbackMessage)
    {
        MaxioStatusCaptureHandler.LastStatus.Value = null;
        try
        {
            return await action();
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw MapJsonException(ex, fallbackMessage);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Billing provider unreachable.");
            throw new BillingProviderException("The billing provider is unreachable.", (int)HttpStatusCode.ServiceUnavailable, ex);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey)
            || string.IsNullOrWhiteSpace(_settings.Subdomain)
            || string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new BillingProviderException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle.",
                (int)HttpStatusCode.ServiceUnavailable);
        }
    }

    private static void GuardShopper(ShopperIdentity shopper)
    {
        if (shopper is null
            || string.IsNullOrWhiteSpace(shopper.UserId)
            || string.IsNullOrWhiteSpace(shopper.Email)
            || string.IsNullOrWhiteSpace(shopper.FirstName)
            || string.IsNullOrWhiteSpace(shopper.LastName))
        {
            throw new BillingProviderException(
                "The signed-in user is missing identity details required to create a billing customer.",
                (int)HttpStatusCode.BadRequest);
        }
    }

    private BillingProviderException MapRawError(RawError raw, string fallback)
    {
        var code = (int)raw.StatusCode;
        if (code == 0)
        {
            code = (int)HttpStatusCode.BadGateway;
        }

        _logger.LogWarning("Maxio HTTP {StatusCode} during billing operation.", code);

        var message = raw.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Billing provider authentication failed.",
            HttpStatusCode.NotFound => fallback,
            _ when code >= 400 && code < 500 => fallback,
            _ => "The billing provider request failed."
        };

        return new BillingProviderException(message, code);
    }

    private static BillingProviderException MapJsonException(JsonException ex, string fallback)
    {
        var status = MaxioStatusCaptureHandler.LastStatus.Value;
        if (status is HttpStatusCode httpStatus && (int)httpStatus >= 400 && (int)httpStatus < 500)
        {
            return new BillingProviderException(fallback, (int)httpStatus, ex);
        }

        return new BillingProviderException(
            "The billing provider returned a response that could not be processed.",
            (int)HttpStatusCode.BadGateway,
            ex);
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        return new SubscriptionPlan(
            Handle: product.Handle!,
            Name: product.Name ?? product.Handle!,
            Description: product.Description,
            PriceInCents: product.PriceInCents ?? 0,
            Interval: product.Interval,
            IntervalUnit: product.IntervalUnit?.Value);
    }

    private static ShopperSubscription MapSubscription(Subscription subscription)
    {
        var price = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;
        var handle = subscription.Product?.Handle ?? string.Empty;
        var name = subscription.Product?.Name ?? handle;

        return new ShopperSubscription(
            Id: subscription.Id ?? 0,
            Reference: subscription.Reference,
            PlanHandle: handle,
            PlanName: name,
            PriceInCents: price,
            State: subscription.State?.Value ?? "unknown",
            NextBillingAt: subscription.NextAssessmentAt,
            CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            Currency: subscription.Currency);
    }

    private static bool IsLive(SubscriptionState? state)
    {
        if (state is null)
        {
            return false;
        }

        return state == SubscriptionState.Pending
            || state == SubscriptionState.Trialing
            || state == SubscriptionState.Assessing
            || state == SubscriptionState.Active
            || state == SubscriptionState.SoftFailure
            || state == SubscriptionState.PastDue
            || state == SubscriptionState.Suspended
            || state == SubscriptionState.Paused
            || state == SubscriptionState.Unpaid
            || state == SubscriptionState.OnHold
            || state == SubscriptionState.AwaitingSignup;
    }
}
