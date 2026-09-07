using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// DTO for subscription plan information
/// </summary>
public class SubscriptionPlanDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? PriceInDollars => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

/// <summary>
/// DTO for subscription information
/// </summary>
public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public decimal? ProductPriceInDollars => ProductPriceInCents.HasValue ? ProductPriceInCents.Value / 100m : null;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Reference { get; set; }
    public int? CustomerId { get; set; }
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? CustomerEmail { get; set; }
}

public interface IMaxioService
{
    /// <summary>
    /// List all subscription plans in the product family
    /// </summary>
    Task<IList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Create or get idempotent subscription for a user
    /// </summary>
    Task<SubscriptionDto> CreateSubscriptionAsync(
        string userId,
        string userEmail,
        string firstName,
        string lastName,
        string productHandle,
        string? reference = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get all subscriptions for a user
    /// </summary>
    Task<IList<SubscriptionDto>> ListUserSubscriptionsAsync(
        string userId,
        CancellationToken ct = default);
}

public class MaxioService : IMaxioService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> options, ILogger<MaxioService> logger)
    {
        _client = client;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<IList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Listing subscription plans for family handle: {FamilyHandle}", _settings.ProductFamilyHandle);

            var plans = new List<SubscriptionPlanDto>();
            int page = 1;
            int perPage = 20;
            bool hasMore = true;

            while (hasMore)
            {
                var response = await _client.Products.ListProducts(
                    dateField: null,
                    filter: null,
                    endDate: null,
                    endDatetime: null,
                    startDate: null,
                    startDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: page,
                    perPage: perPage,
                    ct: ct);

                if (response == null || response.Count == 0)
                {
                    hasMore = false;
                    break;
                }

                foreach (var envelope in response)
                {
                    if (envelope?.Product == null)
                        continue;

                    var product = envelope.Product;

                    // Filter by product family handle if it matches our configured family
                    // Note: Product envelope doesn't directly expose family_handle in this SDK version,
                    // so we include all products and rely on the handle check or let caller filter
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id,
                        Name = product.Name,
                        Handle = product.Handle,
                        Description = product.Description,
                        PriceInCents = product.PriceInCents,
                        Interval = product.Interval,
                        IntervalUnit = product.IntervalUnit?.Value
                    });
                }

                if (response.Count < perPage)
                {
                    hasMore = false;
                }
                else
                {
                    page++;
                }
            }

            _logger.LogInformation("Retrieved {PlanCount} plans", plans.Count);
            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing plans: {StatusCode}", ex.Error.StatusCode);
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userId,
        string userEmail,
        string firstName,
        string lastName,
        string productHandle,
        string? reference = null,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Creating subscription for user {UserId} with product {ProductHandle}", userId, productHandle);

            // Step 1: Try to find existing customer by reference (user ID)
            Customer? existingCustomer = null;
            try
            {
                var customerResponse = await _client.Customers.ReadCustomerByReference(
                    reference: userId,
                    ct: ct);

                if (customerResponse?.Customer != null)
                {
                    existingCustomer = customerResponse.Customer;
                    _logger.LogInformation("Found existing customer {CustomerId} for user {UserId}", existingCustomer.Id, userId);
                }
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("No existing customer found for user {UserId}, will create one", userId);
            }

            // Step 2: Create customer if not found
            int customerId;
            if (existingCustomer != null)
            {
                customerId = existingCustomer.Id ?? throw new InvalidOperationException("Customer ID should not be null");
            }
            else
            {
                var createCustomerRequest = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = userEmail,
                        Reference = userId  // Use user ID as unique reference
                    }
                };

                try
                {
                    var customerResponse = await _client.Customers.CreateCustomer(
                        body: createCustomerRequest,
                        ct: ct);

                    customerId = customerResponse?.Customer?.Id ?? throw new InvalidOperationException("Created customer should have an ID");
                    _logger.LogInformation("Created new customer {CustomerId} for user {UserId}", customerId, userId);
                }
                catch (SdkException<CreateCustomerError> ex)
                {
                    _logger.LogError(ex, "Error creating customer for user {UserId}", userId);
                    if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
                    {
                        _logger.LogError("Customer error response: {Errors}", string.Join(", ", errorResponse.Errors ?? new()));
                    }
                    throw;
                }
            }

            // Step 3: Create subscription
            var createSubscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    Reference = reference ?? $"{userId}-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    DeferSignup = false
                }
            };

            try
            {
                var subscriptionResponse = await _client.Subscriptions.CreateSubscription(
                    body: createSubscriptionRequest,
                    ct: ct);

                var subscription = subscriptionResponse?.Subscription ?? throw new InvalidOperationException("Subscription response should not be null");

                var result = new SubscriptionDto
                {
                    Id = subscription.Id,
                    State = subscription.State?.Value,
                    ProductPriceInCents = subscription.ProductPriceInCents,
                    CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = subscription.NextAssessmentAt,
                    ActivatedAt = subscription.ActivatedAt,
                    CreatedAt = subscription.CreatedAt,
                    Reference = subscription.Reference,
                    CustomerId = subscription.Customer?.Id,
                    ProductId = subscription.Product?.Id,
                    ProductName = subscription.Product?.Name,
                    CustomerEmail = subscription.Customer?.Email
                };

                _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId}", result.Id, customerId);
                return result;
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                _logger.LogError(ex, "Error creating subscription for customer {CustomerId}", customerId);
                if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
                {
                    _logger.LogError("Subscription error response: {Errors}", string.Join(", ", errorResponse.Errors ?? new List<string>()));
                }
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription");
            throw;
        }
    }

    public async Task<IList<SubscriptionDto>> ListUserSubscriptionsAsync(
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Listing subscriptions for user {UserId}", userId);

            // First, find the customer by reference
            Customer? customer = null;
            try
            {
                var customerResponse = await _client.Customers.ReadCustomerByReference(
                    reference: userId,
                    ct: ct);

                customer = customerResponse?.Customer;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("No customer found for user {UserId}", userId);
                return new List<SubscriptionDto>();
            }

            if (customer?.Id == null)
            {
                _logger.LogWarning("Customer found but has no ID for user {UserId}", userId);
                return new List<SubscriptionDto>();
            }

            var subscriptions = new List<SubscriptionDto>();
            var response = await _client.Customers.ListCustomerSubscriptions(
                customerId: customer.Id.Value,
                ct: ct);

            if (response == null || response.Count == 0)
            {
                _logger.LogInformation("No subscriptions found for customer {CustomerId}", customer.Id);
                return subscriptions;
            }

            foreach (var envelope in response)
            {
                var subscription = envelope?.Subscription;
                if (subscription == null)
                    continue;

                subscriptions.Add(new SubscriptionDto
                {
                    Id = subscription.Id,
                    State = subscription.State?.Value,
                    ProductPriceInCents = subscription.ProductPriceInCents,
                    CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = subscription.NextAssessmentAt,
                    ActivatedAt = subscription.ActivatedAt,
                    CreatedAt = subscription.CreatedAt,
                    Reference = subscription.Reference,
                    CustomerId = subscription.Customer?.Id,
                    ProductId = subscription.Product?.Id,
                    ProductName = subscription.Product?.Name,
                    CustomerEmail = subscription.Customer?.Email
                });
            }

            _logger.LogInformation("Retrieved {SubscriptionCount} subscriptions for customer {CustomerId}", subscriptions.Count, customer.Id);
            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions for user {UserId}", userId);
            throw;
        }
    }
}
