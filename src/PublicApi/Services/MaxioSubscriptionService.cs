using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionBillingService
{
    private const string ProductCacheKeyPrefix = "Maxio:ProductFamily:";
    private static readonly TimeSpan ProductCacheDuration = TimeSpan.FromMinutes(5);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        var (_, products) = await GetProductFamilyAndPlansAsync(ct);
        return products.Select(MapPlan).ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string customerReference, string email, string planHandle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(customerReference));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionValidationException("A plan handle is required.");
        }

        var (_, products) = await GetProductFamilyAndPlansAsync(ct);
        var plan = products.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionValidationException($"Unknown subscription plan '{planHandle}'.");
        }

        if (plan.RequireCreditCard == true)
        {
            throw new SubscriptionValidationException($"Plan '{planHandle}' requires a payment method, which this integration does not support.");
        }

        var customerId = await FindOrCreateCustomerIdAsync(customerReference, email, ct);

        var existingSubscriptions = await ListCustomerSubscriptionsInternalAsync(customerId, ct);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && IsLive(s.State));
        if (existing is not null)
        {
            _logger.LogInformation(
                "Customer reference {CustomerReference} already has a live subscription on plan {PlanHandle}; returning the existing subscription instead of creating a duplicate.",
                customerReference, planHandle);
            return MapSubscription(existing);
        }

        // RequireCreditCard=false only gates the hosted signup page's card prompt — it does not mean
        // CreateSubscription can bill a non-zero balance with no payment method under the default
        // (Automatic) collection mode. A non-card collection method is required instead; which one this
        // site's billing architecture accepts (Invoice vs Remittance) isn't knowable ahead of a live call,
        // so try Invoice first and fall back to Remittance on rejection.
        Subscription created;
        try
        {
            created = await CreateSubscriptionCoreAsync(planHandle, customerId, CollectionMethod.Invoice, ct);
        }
        catch (SubscriptionValidationException)
        {
            created = await CreateSubscriptionCoreAsync(planHandle, customerId, CollectionMethod.Remittance, ct);
        }

        return MapSubscription(created);
    }

    private async Task<Subscription> CreateSubscriptionCoreAsync(string planHandle, int customerId, CollectionMethod collectionMethod, CancellationToken ct)
    {
        try
        {
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = collectionMethod
                }
            };
            var response = await _client.Subscriptions.CreateSubscription(body, ct: ct);
            return response.Subscription ?? throw new SubscriptionProviderException("The billing provider returned an empty subscription.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new SubscriptionValidationException(errorList.Errors);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new SubscriptionProviderException($"The billing provider rejected the subscription request: {raw.ReadAsString()}");
            }
            throw new SubscriptionProviderException("The billing provider rejected the subscription request.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionProviderException("The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SubscriptionProviderException("The billing provider is currently unreachable.", ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetCustomerSubscriptionsAsync(string customerReference, CancellationToken ct = default)
    {
        var customerId = await TryReadCustomerIdByReferenceAsync(customerReference, ct);
        if (customerId is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsInternalAsync(customerId.Value, ct);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<(int FamilyId, IReadOnlyList<Product> Products)> GetProductFamilyAndPlansAsync(CancellationToken ct)
    {
        var cacheKey = ProductCacheKeyPrefix + _settings.ProductFamilyHandle;
        if (_cache.TryGetValue(cacheKey, out (int FamilyId, IReadOnlyList<Product> Products) cached))
        {
            return cached;
        }

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw new SubscriptionProviderException($"Unable to list product families from the billing provider: {ex.Error.ReadAsString()}", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionProviderException("The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SubscriptionProviderException("The billing provider is currently unreachable.", ex);
        }

        var match = families.FirstOrDefault(f => string.Equals(f.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
        if (match?.ProductFamily?.Id is not int familyId)
        {
            throw new SubscriptionProviderException($"Configured Maxio product family '{_settings.ProductFamilyHandle}' was not found on this site.");
        }

        List<Product> products;
        try
        {
            var pageProducts = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 200,
                ct: ct);
            products = pageProducts.Select(p => p.Product).Where(p => p is not null).Select(p => p!).ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                throw new SubscriptionProviderException($"Configured Maxio product family '{_settings.ProductFamilyHandle}' was not found: {notFound}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new SubscriptionProviderException($"Unable to list plans from the billing provider: {raw.ReadAsString()}");
            }
            throw new SubscriptionProviderException("Unable to list plans from the billing provider.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionProviderException("The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SubscriptionProviderException("The billing provider is currently unreachable.", ex);
        }

        var result = (familyId, (IReadOnlyList<Product>)products);
        _cache.Set(cacheKey, result, ProductCacheDuration);
        return result;
    }

    private async Task<int> FindOrCreateCustomerIdAsync(string customerReference, string email, CancellationToken ct)
    {
        var existingId = await TryReadCustomerIdByReferenceAsync(customerReference, ct);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var (firstName, lastName) = SplitEmailForName(email);
        try
        {
            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = customerReference
                }
            };
            var response = await _client.Customers.CreateCustomer(body, ct: ct);
            if (response.Customer?.Id is int createdId)
            {
                return createdId;
            }
            throw new SubscriptionProviderException("The billing provider returned an empty customer record.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            string? rawMessage = null;
            if (ex.Error.TryGetRawError(out var raw))
            {
                rawMessage = raw.ReadAsString();
            }
            _logger.LogWarning(
                "Maxio CreateCustomer returned a validation error for reference {CustomerReference} (likely a concurrent duplicate create): {RawMessage}",
                customerReference, rawMessage);

            // A 422 here is almost always "reference already taken" by a concurrent request racing us — re-fetch rather than fail.
            var afterRaceId = await TryReadCustomerIdByReferenceAsync(customerReference, ct);
            if (afterRaceId is not null)
            {
                return afterRaceId.Value;
            }
            throw new SubscriptionProviderException($"Unable to create a billing customer for reference '{customerReference}'.", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionProviderException("The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SubscriptionProviderException("The billing provider is currently unreachable.", ex);
        }
    }

    private async Task<int?> TryReadCustomerIdByReferenceAsync(string customerReference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(customerReference, ct: ct);
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            throw new SubscriptionProviderException($"Unable to look up billing customer: {ex.Error.ReadAsString()}", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionProviderException("The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SubscriptionProviderException("The billing provider is currently unreachable.", ex);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsInternalAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
            return response.Select(r => r.Subscription).Where(s => s is not null).Select(s => s!).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new SubscriptionProviderException($"Unable to list subscriptions from the billing provider: {ex.Error.ReadAsString()}", ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionProviderException("The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SubscriptionProviderException("The billing provider is currently unreachable.", ex);
        }
    }

    private static bool IsLive(SubscriptionState? state)
    {
        if (state is null)
        {
            return false;
        }

        return state != SubscriptionState.Canceled
            && state != SubscriptionState.Expired
            && state != SubscriptionState.FailedToCreate;
    }

    private static (string FirstName, string LastName) SplitEmailForName(string email)
    {
        var local = email.Split('@')[0];
        return (string.IsNullOrWhiteSpace(local) ? "eShopOnWeb" : local, "Subscriber");
    }

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        RequiresPaymentMethod = product.RequireCreditCard == true
    };

    private static CustomerSubscription MapSubscription(Subscription subscription) => new()
    {
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.Product?.PriceInCents,
        State = subscription.State?.Value ?? "unknown",
        NextBillingDate = subscription.CurrentPeriodEndsAt
    };
}
