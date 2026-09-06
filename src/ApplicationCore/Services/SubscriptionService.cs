using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface ISubscriptionService
{
    Task<List<Product>?> GetSubscriptionPlans();
    Task<bool> EnsureCustomerExists(string userId, string firstName, string lastName, string email);
    Task<Subscription?> CreateSubscription(string userId, string productHandle);
    Task<List<Subscription>?> GetUserSubscriptions(string userId);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionService> _logger;
    private const string CUSTOMER_REFERENCE_PREFIX = "eshop_";

    public SubscriptionService(IMaxioClient maxioClient, MaxioSettings settings, ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<Product>?> GetSubscriptionPlans()
    {
        try
        {
            var endpoint = $"/product_families/handle:{_settings.ProductFamilyHandle}/products.json";
            var response = await _maxioClient.GetAsync<ProductsResponse>(endpoint);
            return response?.Products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans");
            return null;
        }
    }

    public async Task<bool> EnsureCustomerExists(string userId, string firstName, string lastName, string email)
    {
        try
        {
            var customerRef = GetCustomerReference(userId);

            var lookupResponse = await _maxioClient.GetAsync<CustomerLookupResponse>(
                $"/customers/lookup.json?reference={Uri.EscapeDataString(customerRef)}"
            );

            if (lookupResponse?.Customer != null)
            {
                _logger.LogInformation("Customer already exists for userId {UserId}, maxioId {CustomerId}",
                    userId, lookupResponse.Customer.Id);
                return true;
            }

            var createRequest = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = customerRef
                }
            };

            var response = await _maxioClient.PostAsync<CustomerResponse>("/customers.json", createRequest);
            if (response?.Customer != null)
            {
                _logger.LogInformation("Created Maxio customer for userId {UserId}, maxioId {CustomerId}",
                    userId, response.Customer.Id);
                return true;
            }

            _logger.LogWarning("Failed to create Maxio customer for userId {UserId}", userId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring customer exists for userId {UserId}", userId);
            return false;
        }
    }

    public async Task<Subscription?> CreateSubscription(string userId, string productHandle)
    {
        try
        {
            var customerRef = GetCustomerReference(userId);

            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new SubscriptionInput
                {
                    ProductHandle = productHandle,
                    CustomerReference = customerRef,
                    PaymentCollectionMethod = "remittance"
                }
            };

            var response = await _maxioClient.PostAsync<SubscriptionResponse>("/subscriptions.json", subscriptionRequest);
            return response?.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for userId {UserId} productHandle {ProductHandle}",
                userId, productHandle);
            return null;
        }
    }

    public async Task<List<Subscription>?> GetUserSubscriptions(string userId)
    {
        try
        {
            var customerRef = GetCustomerReference(userId);

            var lookupResponse = await _maxioClient.GetAsync<CustomerLookupResponse>(
                $"/customers/lookup.json?reference={Uri.EscapeDataString(customerRef)}"
            );

            if (lookupResponse?.Customer == null)
            {
                _logger.LogWarning("Customer not found for userId {UserId}", userId);
                return null;
            }

            var subscriptionsResponse = await _maxioClient.GetAsync<SubscriptionsResponse>(
                $"/customers/{lookupResponse.Customer.Id}/subscriptions.json"
            );

            return subscriptionsResponse?.Subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching user subscriptions for userId {UserId}", userId);
            return null;
        }
    }

    private static string GetCustomerReference(string userId) => $"{CUSTOMER_REFERENCE_PREFIX}{userId}";
}
