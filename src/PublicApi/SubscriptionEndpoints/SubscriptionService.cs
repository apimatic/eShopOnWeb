using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, int productId, CancellationToken ct = default);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly MaxioConfig _config;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        MaxioAdvancedBillingClient maxioClient,
        IOptions<MaxioConfig> options,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _config = options.Value;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _maxioClient.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _config.ProductFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct);

            var plans = response
                .Where(pr => pr.Product?.ArchivedAt == null)
                .Select(pr => new SubscriptionPlanDto
                {
                    Id = pr.Product?.Id ?? 0,
                    Name = pr.Product?.Name ?? "Unknown",
                    Handle = pr.Product?.Handle ?? string.Empty,
                    PricePerMonth = (pr.Product?.PriceInCents ?? 0) / 100m,
                    Description = pr.Product?.Description ?? string.Empty
                })
                .ToList();

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans");
            throw;
        }
    }

    public async Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, int productId, CancellationToken ct = default)
    {
        try
        {
            var customer = await GetOrCreateCustomerAsync(userId, ct);
            if (customer?.Id == null)
            {
                _logger.LogWarning("Could not create or find customer for user {UserId}", userId);
                return null;
            }

            var request = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    ProductId = productId,
                    CustomerId = customer.Id,
                    Reference = $"sub-{userId}-{Guid.NewGuid()}",
                    PaymentCollectionMethod = null,
                    PaymentProfileAttributes = null
                }
            };

            var response = await _maxioClient.Subscriptions.CreateSubscription(request, ct: ct);

            if (response?.Subscription != null)
            {
                return new SubscriptionDto
                {
                    Id = response.Subscription.Id ?? 0,
                    CustomerId = response.Subscription.Customer?.Id ?? 0,
                    ProductId = response.Subscription.Product?.Id ?? 0,
                    ProductName = response.Subscription.Product?.Name ?? "Plan",
                    State = response.Subscription.State?.Value ?? "unknown",
                    CreatedAt = response.Subscription.CreatedAt ?? DateTimeOffset.UtcNow,
                    CurrentPeriodEndsAt = response.Subscription.CurrentPeriodEndsAt
                };
            }

            return null;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var errors = string.Join("; ", errorList.Errors ?? new List<string>());
                _logger.LogError(ex, "Subscription creation failed for user {UserId}: {Errors}", userId, errors);
            }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var customer = await GetCustomerByReferenceAsync(userId, ct);
            if (customer?.Id == null)
            {
                return new List<SubscriptionDto>();
            }

            var subscriptions = await _maxioClient.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct);

            var result = subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => new SubscriptionDto
                {
                    Id = s.Subscription?.Id ?? 0,
                    CustomerId = s.Subscription?.Customer?.Id ?? 0,
                    ProductId = s.Subscription?.Product?.Id ?? 0,
                    ProductName = s.Subscription?.Product?.Name ?? "Plan",
                    State = s.Subscription?.State?.Value ?? "unknown",
                    CreatedAt = s.Subscription?.CreatedAt ?? DateTimeOffset.UtcNow,
                    CurrentPeriodEndsAt = s.Subscription?.CurrentPeriodEndsAt
                })
                .ToList();

            return result;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for user {UserId}", userId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for user {UserId}", userId);
            throw;
        }
    }

    private async Task<Customer?> GetOrCreateCustomerAsync(string userId, CancellationToken ct)
    {
        var existing = await GetCustomerByReferenceAsync(userId, ct);
        if (existing != null)
            return existing;

        try
        {
            var request = new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                {
                    FirstName = "User",
                    LastName = userId.Split('@')[0],
                    Email = $"{userId}@eshop.local",
                    Reference = userId
                }
            };

            var response = await _maxioClient.Customers.CreateCustomer(request, ct: ct);
            return response?.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResp))
            {
                var errors = string.Join("; ", errorResp.Errors?.PerPage ?? new List<string>());
                _logger.LogWarning(ex, "Customer creation failed for user {UserId}: {Errors}", userId, errors);
            }
            return null;
        }
    }

    private async Task<Customer?> GetCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _maxioClient.Customers.ReadCustomerByReference(reference, ct: ct);
            return response?.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            _logger.LogWarning(ex, "Error reading customer by reference {Reference}", reference);
            return null;
        }
    }
}
