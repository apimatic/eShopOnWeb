using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly MaxioApiClient _apiClient;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioApiClient apiClient, IAppLogger<MaxioBillingService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<MaxioCustomerInfo?> GetOrCreateCustomerAsync(string userId, string firstName, string lastName, string email)
    {
        try
        {
            var lookupResponse = await _apiClient.GetAsync<CustomerResponse>($"customers/lookup.json?reference={Uri.EscapeDataString(userId)}");
            if (lookupResponse?.Customer != null)
            {
                _logger.LogInformation($"Found existing Maxio customer for userId {userId}: {lookupResponse.Customer.Id}");
                return MapCustomerDtoToInfo(lookupResponse.Customer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"Customer lookup failed (expected for new users): {ex.Message}");
        }

        _logger.LogInformation($"Creating new Maxio customer for userId {userId}");
        var request = new CreateCustomerRequest
        {
            Customer = new CustomerData
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId
            }
        };

        var response = await _apiClient.PostAsync<CustomerResponse>("customers.json", request);
        if (response?.Customer != null)
        {
            _logger.LogInformation($"Created Maxio customer {response.Customer.Id} for userId {userId}");
            return MapCustomerDtoToInfo(response.Customer);
        }

        _logger.LogWarning($"Failed to create customer for userId {userId}");
        return null;
    }

    public async Task<MaxioSubscriptionInfo?> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            _logger.LogInformation($"Creating subscription for customerId {customerId}, productHandle {productHandle}");
            var request = new CreateSubscriptionRequest
            {
                Subscription = new SubscriptionData
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            };

            var response = await _apiClient.PostAsync<SubscriptionResponse>("subscriptions.json", request);
            if (response?.Subscription != null)
            {
                _logger.LogInformation($"Created subscription {response.Subscription.Id}");
                return MapSubscriptionDtoToInfo(response.Subscription);
            }

            _logger.LogWarning($"Failed to create subscription for customerId {customerId}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error creating subscription: {ex.Message}");
            throw;
        }
    }

    public async Task<List<MaxioSubscriptionInfo>> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            _logger.LogInformation($"Listing subscriptions for customerId {customerId}");
            var response = await _apiClient.GetAsync<SubscriptionsResponse>($"customers/{customerId}/subscriptions.json");

            if (response?.Subscriptions == null || response.Subscriptions.Length == 0)
            {
                return new List<MaxioSubscriptionInfo>();
            }

            return response.Subscriptions
                .Select(MapSubscriptionDtoToInfo)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error listing subscriptions: {ex.Message}");
            return new List<MaxioSubscriptionInfo>();
        }
    }

    public async Task<List<MaxioProductInfo>> ListProductsAsync()
    {
        try
        {
            _logger.LogInformation("Listing products");
            var response = await _apiClient.GetAsync<ProductsResponse>("products.json");

            if (response?.Products == null || response.Products.Length == 0)
            {
                return new List<MaxioProductInfo>();
            }

            return response.Products
                .Select(MapProductDtoToInfo)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error listing products: {ex.Message}");
            return new List<MaxioProductInfo>();
        }
    }

    private static MaxioCustomerInfo MapCustomerDtoToInfo(CustomerDto dto)
    {
        return new MaxioCustomerInfo
        {
            Id = dto.Id,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email
        };
    }

    private static MaxioSubscriptionInfo MapSubscriptionDtoToInfo(SubscriptionDto dto)
    {
        var billingPeriod = "monthly";
        var pricePerCycle = dto.Product?.PriceInCents ?? 0;

        if (dto.Product != null)
        {
            billingPeriod = $"{dto.Product.Interval} {dto.Product.IntervalUnit}(s)";
        }

        return new MaxioSubscriptionInfo
        {
            Id = dto.Id,
            CustomerId = dto.CustomerId,
            State = dto.State,
            ProductName = dto.Product?.Name ?? "Unknown",
            ProductHandle = dto.Product?.Handle ?? string.Empty,
            PricePerBillingCycle = pricePerCycle / 100m,
            BillingPeriod = billingPeriod,
            NextBillingAt = dto.NextBillingAt
        };
    }

    private static MaxioProductInfo MapProductDtoToInfo(ProductDto dto)
    {
        var billingPeriod = $"{dto.Interval} {dto.IntervalUnit}(s)";
        return new MaxioProductInfo
        {
            Id = dto.Id,
            Name = dto.Name,
            Handle = dto.Handle,
            Description = dto.Description ?? string.Empty,
            PricePerBillingCycle = dto.PriceInCents / 100m,
            BillingPeriod = billingPeriod
        };
    }
}
