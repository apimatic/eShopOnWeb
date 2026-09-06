using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly IRepository<MaxioCustomerMapping> _customerMappingRepository;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioClient maxioClient,
        IRepository<MaxioCustomerMapping> customerMappingRepository,
        MaxioSettings settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _customerMappingRepository = customerMappingRepository;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<MaxioProductDto>> GetAvailablePlansAsync()
    {
        try
        {
            var plans = await _maxioClient.ListProductsByFamilyHandleAsync(_settings.ProductFamilyHandle);
            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching available plans");
            throw;
        }
    }

    public async Task<List<MaxioSubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        try
        {
            var spec = new MaxioCustomerMappingByUserIdSpecification(userId);
            var mapping = await _customerMappingRepository.FirstOrDefaultAsync(spec);

            if (mapping == null)
            {
                _logger.LogInformation("No Maxio customer mapping found for user: {UserId}", userId);
                return new List<MaxioSubscriptionDto>();
            }

            var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(mapping.MaxioCustomerId);
            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(
        string userId,
        string userEmail,
        string firstName,
        string lastName,
        string planHandle)
    {
        try
        {
            var maxioCustomer = await EnsureMaxioCustomerExistsAsync(userId, userEmail, firstName, lastName);

            var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(maxioCustomer.Id);
            var existingSubscription = existingSubscriptions.FirstOrDefault(s =>
                s.ProductName.Contains(planHandle, StringComparison.OrdinalIgnoreCase) ||
                s.State == "active");

            if (existingSubscription != null)
            {
                _logger.LogInformation("Customer {CustomerId} already has active subscription for plan {PlanHandle}",
                    maxioCustomer.Id, planHandle);
                return existingSubscription;
            }

            var subscription = await _maxioClient.CreateSubscriptionAsync(
                maxioCustomer.Id,
                planHandle,
                paymentCollectionMethod: "automatic"
            );

            _logger.LogInformation("Successfully created subscription {SubscriptionId} for customer {CustomerId}",
                subscription.Id, maxioCustomer.Id);

            return subscription;
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio API error creating subscription for user {UserId}: {Message}",
                userId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription for user {UserId}", userId);
            throw;
        }
    }

    private async Task<MaxioCustomerDto> EnsureMaxioCustomerExistsAsync(
        string userId,
        string userEmail,
        string firstName,
        string lastName)
    {
        var spec = new MaxioCustomerMappingByUserIdSpecification(userId);
        var existingMapping = await _customerMappingRepository.FirstOrDefaultAsync(spec);

        if (existingMapping != null)
        {
            var customer = await _maxioClient.GetCustomerAsync(existingMapping.MaxioCustomerId);
            if (customer != null)
            {
                _logger.LogInformation("Found existing Maxio customer {CustomerId} for user {UserId}",
                    customer.Id, userId);
                return customer;
            }
        }

        _logger.LogInformation("Creating new Maxio customer for user {UserId}", userId);

        var newCustomer = await _maxioClient.CreateCustomerAsync(
            userEmail,
            firstName,
            lastName,
            reference: userId
        );

        var mapping = new MaxioCustomerMapping
        {
            ApplicationUserId = userId,
            MaxioCustomerId = newCustomer.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _customerMappingRepository.AddAsync(mapping);

        _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}",
            newCustomer.Id, userId);

        return newCustomer;
    }
}
