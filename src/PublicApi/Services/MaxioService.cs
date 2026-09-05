using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Authentication;
using AdvancedBilling.Standard.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<List<Product>> ListProductsAsync();
    Task<Customer> GetOrCreateCustomerAsync(string userId, string email);
    Task<Subscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<Subscription>> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioService : IMaxioService
{
    private readonly AdvancedBillingClient _client;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioService> _logger;
    private readonly IMemoryCache _cache;
    private const string CustomerCacheKeyPrefix = "maxio_customer_";
    private const string ProductsCacheKey = "maxio_products_";

    public MaxioService(IOptions<MaxioConfiguration> options, ILogger<MaxioService> logger, IMemoryCache cache)
    {
        _config = options.Value;
        _logger = logger;
        _cache = cache;

        if (string.IsNullOrEmpty(_config.ApiKey) || string.IsNullOrEmpty(_config.Subdomain))
        {
            throw new InvalidOperationException("Maxio configuration is missing required fields (ApiKey, Subdomain).");
        }

        var builder = new AdvancedBillingClient.Builder()
            .BasicAuthCredentials(
                new BasicAuthModel.Builder(_config.ApiKey, "x").Build())
            .Environment(AdvancedBilling.Standard.Environment.US)
            .Site(_config.Subdomain);

        _client = builder.Build();
    }

    public async Task<List<Product>> ListProductsAsync()
    {
        try
        {
            var input = new ListProductsInput();
            var products = await _client.ProductsController.ListProductsAsync(input);
            return products?.Select(p => p.Product).Where(p => p != null).ToList() ?? new List<Product>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products from Maxio");
            throw;
        }
    }

    public async Task<Customer> GetOrCreateCustomerAsync(string userId, string email)
    {
        try
        {
            var cacheKey = $"{CustomerCacheKeyPrefix}{userId}";

            // Check if customer already exists in cache
            if (_cache.TryGetValue(cacheKey, out Customer? cachedCustomer) && cachedCustomer != null)
            {
                return cachedCustomer;
            }

            // Ensure email is valid
            var validEmail = email;
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            {
                validEmail = $"user-{Guid.NewGuid().ToString().Substring(0, 8)}@eshop.local";
            }

            // Use userId + short GUID as reference to ensure uniqueness across runs but consistency within cache
            var reference = $"{userId}-{Guid.NewGuid().ToString().Substring(0, 8)}";

            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "User",
                    LastName = userId.Length > 30 ? userId.Substring(0, 30) : userId,
                    Email = validEmail,
                    Reference = reference
                }
            };

            var response = await _client.CustomersController.CreateCustomerAsync(createRequest);
            var customer = response.Customer;

            // Cache the customer for future use
            _cache.Set(cacheKey, customer, TimeSpan.FromHours(1));

            return customer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating customer {UserId}", userId);
            throw;
        }
    }

    public async Task<Subscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var product = await GetProductByHandleAsync(productHandle);
            if (product == null)
            {
                throw new InvalidOperationException($"Product '{productHandle}' not found");
            }

            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            };

            var response = await _client.SubscriptionsController.CreateSubscriptionAsync(createRequest);
            return response.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer {CustomerId} with product {ProductHandle}", customerId, productHandle);
            throw;
        }
    }

    public async Task<List<Subscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var subscriptionResponses = await _client.CustomersController.ListCustomerSubscriptionsAsync(customerId);
            return subscriptionResponses?.Select(sr => sr.Subscription).Where(s => s != null).ToList() ?? new List<Subscription>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing customer subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    private async Task<Product?> GetProductByHandleAsync(string handle)
    {
        try
        {
            var input = new ListProductsInput();
            var products = await _client.ProductsController.ListProductsAsync(input);
            return products?.Select(p => p.Product).FirstOrDefault(p => p?.Handle == handle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding product by handle {Handle}", handle);
            return null;
        }
    }
}
