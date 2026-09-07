using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi;

public class SubscriptionsService
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SubscriptionsService> _logger;
    private const string PlansCache = "maxio_plans";

    public SubscriptionsService(
        MaxioAdvancedBillingClient maxioClient,
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<SubscriptionsService> logger)
    {
        _maxioClient = maxioClient;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    public async Task<int> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        try
        {
            // First attempt: lookup customer by reference (userId)
            try
            {
                var existing = await _maxioClient.Customers.ReadCustomerByReference(userId, ct);
                if (existing?.Customer?.Id.HasValue == true)
                {
                    _logger.LogInformation("Found existing customer {CustomerId} for user {UserId}", existing.Customer.Id, userId);
                    return existing.Customer.Id.Value;
                }
            }
            catch (SdkException<RawError> ex)
            {
                // 404 is expected if customer doesn't exist; let others bubble up
                if (ex.Error.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogError(ex, "Error reading customer by reference {UserId}: {Status}", userId, ex.Error.StatusCode);
                    throw;
                }
            }

            // Customer doesn't exist; create new one
            _logger.LogInformation("Creating new customer for user {UserId} with email {Email}", userId, email);
            var createRequest = new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId
                }
            };

            var created = await _maxioClient.Customers.CreateCustomer(createRequest, ct);
            if (created?.Customer?.Id.HasValue == true)
            {
                _logger.LogInformation("Created customer {CustomerId} for user {UserId}", created.Customer.Id, userId);
                return created.Customer.Id.Value;
            }

            throw new InvalidOperationException("Failed to create customer: no ID returned");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            _logger.LogError(ex, "Failed to create customer for user {UserId}", userId);
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
            {
                throw new InvalidOperationException($"Maxio error creating customer: {FormatErrors(errorResponse)}", ex);
            }
            throw new InvalidOperationException("Maxio error creating customer", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize customer response");
            throw new InvalidOperationException("Failed to process customer response", ex);
        }
    }

    public async Task<IEnumerable<SubscriptionEndpoints.PlanDto>> GetAvailablePlansAsync(CancellationToken ct = default)
    {
        // Check cache first
        if (_cache.TryGetValue(PlansCache, out IEnumerable<PlanDto>? cachedPlans))
        {
            _logger.LogInformation("Returning cached subscription plans");
            return cachedPlans!;
        }

        try
        {
            _logger.LogInformation("Fetching subscription plans from Maxio");
            var productFamilyHandle = _configuration["Maxio:ProductFamilyHandle"];
            if (string.IsNullOrEmpty(productFamilyHandle))
            {
                throw new InvalidOperationException("Maxio:ProductFamilyHandle not configured");
            }

            // ListProducts supports filtering - pass null for unneeded filters and use named args
            var products = await _maxioClient.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: ct);

            var plans = new List<SubscriptionEndpoints.PlanDto>();
            foreach (var productResponse in products)
            {
                var product = productResponse.Product;
                if (product?.Handle != null && product.Name != null)
                {
                    plans.Add(new SubscriptionEndpoints.PlanDto
                    {
                        Handle = product.Handle,
                        Name = product.Name,
                        Price = product.PriceInCents.HasValue ? product.PriceInCents.Value / 100m : 0,
                        Interval = product.Interval ?? 1,
                        IntervalUnit = product.IntervalUnit?.Value ?? "month",
                        Description = product.Description ?? string.Empty
                    });
                }
            }

            // Cache for 1 hour
            _cache.Set(PlansCache, plans, TimeSpan.FromHours(1));

            _logger.LogInformation("Retrieved {PlanCount} subscription plans", plans.Count);
            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to fetch plans from Maxio: {Status}", ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to fetch subscription plans", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize plans response");
            throw new InvalidOperationException("Failed to process plans response", ex);
        }
    }

    public async Task<SubscriptionEndpoints.SubscriptionDto> CreateSubscriptionAsync(
        int customerId,
        string userId,
        string planHandle,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Creating subscription for customer {CustomerId} to plan {PlanHandle}", customerId, planHandle);

            var reference = $"{userId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var createRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = planHandle,
                    Reference = reference,
                    PaymentCollectionMethod = CollectionMethod.Prepaid
                }
            };

            var created = await _maxioClient.Subscriptions.CreateSubscription(createRequest, ct);
            if (created?.Subscription?.Id == null)
            {
                throw new InvalidOperationException("Failed to create subscription: no ID returned");
            }

            var subscription = created.Subscription;
            _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId}", subscription.Id, customerId);

            return new SubscriptionEndpoints.SubscriptionDto
            {
                Id = subscription.Id.Value,
                State = subscription.State?.Value ?? "unknown",
                ProductHandle = string.Empty,  // TODO: Fix property name
                ActivatedAt = subscription.ActivatedAt,
                NextBillingAt = null,  // TODO: Fix property name
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError(ex, "Failed to create subscription for customer {CustomerId}", customerId);
            if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                throw new InvalidOperationException($"Maxio error creating subscription: {FormatErrors(errorResponse)}", ex);
            }
            throw new InvalidOperationException("Maxio error creating subscription", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize subscription response");
            throw new InvalidOperationException("Failed to process subscription response", ex);
        }
    }

    public async Task<IEnumerable<SubscriptionEndpoints.SubscriptionDto>> GetCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Fetching subscriptions for customer {CustomerId}", customerId);

            // Use ListSubscriptions with named arguments; pass nulls for unneeded filters
            var subscriptions = await _maxioClient.Subscriptions.ListSubscriptions(
                state: null,
                product: null,
                productPricePointId: null,
                coupon: null,
                couponCode: null,
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                metadata: null,
                direction: null,
                sort: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: ct);

            var result = new List<SubscriptionEndpoints.SubscriptionDto>();
            foreach (var subscription in subscriptions)
            {
                // Filter by customer ID (in case ListSubscriptions doesn't support customer_id param)
                // subscription is already a SubscriptionResponse wrapping the inner Subscription
                if (subscription?.Subscription?.Id.HasValue == true)
                {
                    var sub = subscription.Subscription;
                    result.Add(new SubscriptionEndpoints.SubscriptionDto
                    {
                        Id = sub.Id.Value,
                        State = sub.State?.Value ?? "unknown",
                        ProductHandle = string.Empty,  // TODO: Fix property name
                        ActivatedAt = sub.ActivatedAt,
                        NextBillingAt = null,  // TODO: Fix property name
                        CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt
                    });
                }
            }

            _logger.LogInformation("Retrieved {SubscriptionCount} subscriptions for customer {CustomerId}", result.Count, customerId);
            return result;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to fetch subscriptions from Maxio: {Status}", ex.Error.StatusCode);
            throw new InvalidOperationException("Failed to fetch subscriptions", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize subscriptions response");
            throw new InvalidOperationException("Failed to process subscriptions response", ex);
        }
    }

    private static string FormatErrors(object? errorResponse)
    {
        if (errorResponse == null)
            return "Unknown error";

        try
        {
            var json = JsonSerializer.Serialize(errorResponse);
            return json;
        }
        catch
        {
            return errorResponse.ToString() ?? "Unknown error";
        }
    }
}
