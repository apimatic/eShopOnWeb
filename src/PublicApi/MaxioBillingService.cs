using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Exceptions;
using AdvancedBilling.Standard.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

public class MaxioCustomerStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _userToCustomerId = new();

    public bool TryGetCustomerId(string userId, out int customerId)
        => _userToCustomerId.TryGetValue(userId, out customerId);

    public void Store(string userId, int customerId)
    {
        _userToCustomerId[userId] = customerId;
    }
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly AdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly MaxioCustomerStore _customerStore;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        AdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        MaxioCustomerStore customerStore,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _customerStore = customerStore;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync()
    {
        try
        {
            var input = new ListProductsForProductFamilyInput
            {
                ProductFamilyId = "handle:" + _options.ProductFamilyHandle,
                Page = 1,
                PerPage = 100
            };

            var products = await _client.ProductFamiliesController.ListProductsForProductFamilyAsync(input);

            return products
                .Where(p => p.Product?.ArchivedAt == null)
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Product!.Id ?? 0,
                    Name = p.Product.Name ?? string.Empty,
                    Handle = p.Product.Handle ?? string.Empty,
                    Description = p.Product.Description ?? string.Empty,
                    PriceInCents = (int)(p.Product.PriceInCents ?? 0),
                    IntervalUnit = p.Product.IntervalUnit?.ToString() ?? "month",
                    Interval = p.Product.Interval ?? 0,
                    ProductFamilyHandle = p.Product.ProductFamily?.Handle ?? _options.ProductFamilyHandle,
                    ProductFamilyName = p.Product.ProductFamily?.Name ?? string.Empty
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Maxio plans for family {Family}", _options.ProductFamilyHandle);
            throw;
        }
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userId, string email, string firstName, string lastName, string productHandle)
    {
        var customer = await EnsureCustomerAsync(userId, email, firstName, lastName);
        return await CreateSubscriptionIfNotExistsAsync(userId, customer, productHandle);
    }

    public async Task<IReadOnlyList<SubscriptionResult>> GetMySubscriptionsAsync(string userId)
    {
        if (!_customerStore.TryGetCustomerId(userId, out var customerId))
            return Array.Empty<SubscriptionResult>();

        try
        {
            var subscriptions = await _client.CustomersController.ListCustomerSubscriptionsAsync(customerId);
            return subscriptions
                .Where(s => s.Subscription != null)
                .Select(MapSubscription)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    private async Task<CustomerResponse> EnsureCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        // Try local cache
        if (_customerStore.TryGetCustomerId(userId, out var cachedId))
        {
            try
            {
                var cached = await _client.CustomersController.ReadCustomerAsync(cachedId);
                if (cached?.Customer != null) return cached;
            }
            catch { /* customer may have been purged */ }
        }

        // Search by reference
        try
        {
            var found = await _client.CustomersController.ReadCustomerByReferenceAsync(userId);
            if (found?.Customer != null)
            {
                _customerStore.Store(userId, found.Customer.Id ?? 0);
                return found;
            }
        }
        catch (ApiException)
        {
            // 404 Not Found — will create below
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error looking up customer by reference {Reference}", userId);
        }

        // Create
        var response = await _client.CustomersController.CreateCustomerAsync(new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId,
                Organization = "eShopOnWeb"
            }
        });

        if (response?.Customer == null)
            throw new InvalidOperationException("Maxio returned null customer after creation.");

        _customerStore.Store(userId, response.Customer.Id ?? 0);
        _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", response.Customer.Id, userId);
        return response;
    }

    private async Task<SubscriptionResult> CreateSubscriptionIfNotExistsAsync(
        string userId, CustomerResponse customer, string productHandle)
    {
        var maxioCustomerId = customer.Customer?.Id
            ?? throw new InvalidOperationException("Customer ID is null.");

        // Check for existing active subscription
        try
        {
            var existingSubs = await _client.CustomersController.ListCustomerSubscriptionsAsync(maxioCustomerId);
            var existing = existingSubs.FirstOrDefault(s =>
                s.Subscription?.State == SubscriptionState.Active &&
                s.Subscription.Product?.Handle == productHandle);

            if (existing?.Subscription != null)
            {
                _logger.LogInformation("User {UserId} already has subscription {SubId}", userId, existing.Subscription.Id);
                return MapSubscription(existing);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking existing subscriptions for customer {CustomerId}", maxioCustomerId);
        }

        // Create new subscription
        var response = await _client.SubscriptionsController.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = maxioCustomerId,
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        });

        if (response?.Subscription == null)
            throw new InvalidOperationException("Maxio returned null subscription after creation.");

        _logger.LogInformation("Created subscription {SubId} for user {UserId}", response.Subscription.Id, userId);
        return MapSubscription(response);
    }

    private static SubscriptionResult MapSubscription(SubscriptionResponse subResponse)
    {
        var sub = subResponse.Subscription
            ?? throw new InvalidOperationException("Subscription is null in response.");

        return new SubscriptionResult
        {
            Id = sub.Id ?? 0,
            State = sub.State?.ToString() ?? "unknown",
            ProductId = sub.Product?.Id ?? 0,
            ProductName = sub.Product?.Name ?? string.Empty,
            ProductHandle = sub.Product?.Handle ?? string.Empty,
            ProductPriceInCents = (int)(sub.Product?.PriceInCents ?? 0),
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CreatedAt = sub.CreatedAt ?? default,
            CustomerId = sub.Customer?.Id ?? 0,
            CustomerReference = sub.Customer?.Reference ?? string.Empty,
            ProductFamilyName = sub.Product?.ProductFamily?.Name ?? string.Empty
        };
    }
}
