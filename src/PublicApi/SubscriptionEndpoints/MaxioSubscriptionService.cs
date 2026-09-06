using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IConfiguration config,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var productFamilyHandle = _config["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";

            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: productFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct);

            var plans = new List<SubscriptionPlanDto>();
            foreach (var productResponse in products)
            {
                var product = productResponse.Product;
                if (product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Handle = product.Handle ?? string.Empty,
                        Name = product.Name ?? string.Empty,
                        Description = product.Description ?? string.Empty,
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 1,
                        IntervalUnit = product.IntervalUnit?.ToString() ?? "month"
                    });
                }
            }

            return plans;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            _logger.LogError(ex, "Error listing subscription plans");
            if (ex.Error.TryGetString(out var errorMsg))
            {
                throw new InvalidOperationException($"Failed to list subscription plans: {errorMsg}", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                throw new InvalidOperationException($"Failed to list subscription plans: {rawError.StatusCode}", ex);
            }
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse subscription plans response");
            throw new InvalidOperationException("Failed to parse subscription plans response", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscription plans");
            throw;
        }
    }

    public async Task<CustomerDto> EnsureCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId
                }
            };

            var customerResponse = await _client.Customers.CreateCustomer(createRequest, ct: ct);
            var customer = customerResponse.Customer;

            if (customer?.Id == null)
            {
                throw new InvalidOperationException("Failed to create customer: no ID returned");
            }

            return new CustomerDto
            {
                Id = customer.Id.Value,
                Reference = customer.Reference ?? userId,
                Email = customer.Email ?? email
            };
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetRawError(out var rawError))
            {
                var errorMsg = rawError.ReadAsString();
                if (errorMsg.Contains("reference") || errorMsg.Contains("duplicate"))
                {
                    _logger.LogInformation("Customer with reference {UserId} already exists, retrieving existing customer", userId);
                    return await GetCustomerByReferenceAsync(userId, ct);
                }
                throw new InvalidOperationException($"Failed to create customer: HTTP {rawError.StatusCode}: {errorMsg}", ex);
            }
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse customer response");
            throw new InvalidOperationException("Failed to parse customer response", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error ensuring customer");
            throw;
        }
    }

    public async Task<CustomerDto> GetCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        try
        {
            var customerResponse = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            var customer = customerResponse.Customer;

            if (customer?.Id == null)
            {
                throw new InvalidOperationException($"Customer with reference {reference} not found");
            }

            return new CustomerDto
            {
                Id = customer.Id.Value,
                Reference = customer.Reference ?? reference,
                Email = customer.Email ?? string.Empty
            };
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"Customer with reference {reference} not found", ex);
            }
            throw new InvalidOperationException($"Failed to retrieve customer: HTTP {ex.Error.StatusCode}", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default)
    {
        try
        {
            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            };

            var subscriptionResponse = await _client.Subscriptions.CreateSubscription(createRequest, ct: ct);
            var subscription = subscriptionResponse.Subscription;

            if (subscription?.Id == null)
            {
                throw new InvalidOperationException("Failed to create subscription: no ID returned");
            }

            return new SubscriptionDto
            {
                Id = subscription.Id.Value,
                CustomerId = customerId,
                ProductId = 0,
                State = "active",
                CurrentPeriodEndsAt = null,
                NextAssessmentAt = null,
                ActivatedAt = subscription.ActivatedAt,
                CreatedAt = subscription.CreatedAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetRawError(out var rawError))
            {
                throw new InvalidOperationException($"Failed to create subscription: HTTP {rawError.StatusCode}", ex);
            }
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse subscription response");
            throw new InvalidOperationException("Failed to parse subscription response", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription");
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);

            var result = new List<SubscriptionDto>();
            foreach (var subscriptionResponse in subscriptions)
            {
                var subscription = subscriptionResponse.Subscription;
                if (subscription?.Id != null)
                {
                    result.Add(new SubscriptionDto
                    {
                        Id = subscription.Id.Value,
                        CustomerId = customerId,
                        ProductId = 0,
                        State = "active",
                        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                        NextAssessmentAt = subscription.NextAssessmentAt,
                        ActivatedAt = subscription.ActivatedAt,
                        CreatedAt = subscription.CreatedAt
                    });
                }
            }

            return result;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return new List<SubscriptionDto>();
            }
            throw new InvalidOperationException($"Failed to list subscriptions: HTTP {ex.Error.StatusCode}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscriptions");
            throw;
        }
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class CustomerDto
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
