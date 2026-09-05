using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using CreateSubscriptionRequestModel = MaxioAdvancedBilling.Models.CreateSubscriptionRequest;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto?> CreateSubscriptionAsync(string userEmail, string productHandle, CancellationToken ct = default);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userEmail, CancellationToken ct = default);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
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
                page: 1,
                perPage: 100,
                ct: ct);

            var plans = new List<SubscriptionPlanDto>();

            foreach (var productResponse in response)
            {
                var product = productResponse.Product;
                if (product?.Handle == _options.ProductFamilyHandle && product.ProductFamily?.Handle == _options.ProductFamilyHandle)
                {
                    continue;
                }

                if (product?.ProductFamily?.Handle == _options.ProductFamilyHandle)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Name = product.Name ?? string.Empty,
                        Handle = product.Handle ?? string.Empty,
                        Description = product.Description ?? string.Empty,
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 1,
                        IntervalUnit = product.IntervalUnit?.Value ?? "month"
                    });
                }
            }

            return plans;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing subscription plans: {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException($"Failed to list subscription plans: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscription plans");
            throw;
        }
    }

    public async Task<SubscriptionDto?> CreateSubscriptionAsync(string userEmail, string productHandle, CancellationToken ct = default)
    {
        try
        {
            // Step 1: Try to find existing customer by email
            int customerId;
            try
            {
                var customerResponse = await _client.Customers.ReadCustomerByReference(userEmail, ct: ct);
                customerId = customerResponse.Customer?.Id ?? throw new InvalidOperationException("Customer ID not found in response");
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                // Step 2: Customer not found, create new one
                var createCustomerRequest = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        Email = userEmail,
                        Reference = userEmail,
                        FirstName = "Customer",
                        LastName = ""
                    }
                };

                try
                {
                    var newCustomerResponse = await _client.Customers.CreateCustomer(createCustomerRequest, ct: ct);
                    customerId = newCustomerResponse.Customer?.Id ?? throw new InvalidOperationException("Customer ID not found in response");
                }
                catch (SdkException<CreateCustomerError> createEx)
                {
                    if (createEx.Error.TryGetCustomerErrorResponse1(out var validationError))
                    {
                        var msgs = new List<string>();
                        if (validationError.Errors?.PerPage != null)
                        {
                            msgs.AddRange(validationError.Errors.PerPage);
                        }
                        if (validationError.Errors?.PricePoint != null)
                        {
                            msgs.AddRange(validationError.Errors.PricePoint);
                        }
                        var errorMessages = msgs.Count > 0 ? string.Join(", ", msgs) : "Validation error";
                        _logger.LogError("Customer creation validation error: {Errors}", errorMessages);
                        throw new InvalidOperationException($"Failed to create customer: {errorMessages}", createEx);
                    }
                    else if (createEx.Error.TryGetRawError(out var rawError))
                    {
                        _logger.LogError("Customer creation error: {StatusCode} {Body}", rawError.StatusCode, rawError.ReadAsString());
                        throw new InvalidOperationException($"Failed to create customer: {rawError.ReadAsString()}", createEx);
                    }
                    throw;
                }
            }

            // Step 3: Create subscription
            var subscriptionRef = $"{userEmail}-{productHandle}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var createSubscriptionRequest = new CreateSubscriptionRequestModel
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    Reference = subscriptionRef
                }
            };

            try
            {
                var subscriptionResponse = await _client.Subscriptions.CreateSubscription(createSubscriptionRequest, ct: ct);
                var subscription = subscriptionResponse.Subscription;

                return new SubscriptionDto
                {
                    Id = subscription?.Id ?? 0,
                    State = subscription?.State?.Value ?? string.Empty,
                    ProductPriceInCents = subscription?.ProductPriceInCents ?? 0,
                    NextAssessmentAt = subscription?.NextAssessmentAt,
                    ActivatedAt = subscription?.ActivatedAt,
                    CreatedAt = subscription?.CreatedAt,
                    Product = subscription?.Product != null ? new SubscriptionPlanDto
                    {
                        Id = subscription.Product.Id ?? 0,
                        Name = subscription.Product.Name ?? string.Empty,
                        Handle = subscription.Product.Handle ?? string.Empty,
                        Description = subscription.Product.Description ?? string.Empty,
                        PriceInCents = subscription.Product.PriceInCents ?? 0,
                        Interval = subscription.Product.Interval ?? 1,
                        IntervalUnit = subscription.Product.IntervalUnit?.Value ?? "month"
                    } : null
                };
            }
            catch (SdkException<CreateSubscriptionError> subEx)
            {
                if (subEx.Error.TryGetErrorListResponse1(out var errorList))
                {
                    var errors = string.Join(", ", errorList.Errors ?? new List<string>());
                    _logger.LogError("Subscription creation error: {Errors}", errors);
                    throw new InvalidOperationException($"Failed to create subscription: {errors}", subEx);
                }
                else if (subEx.Error.TryGetRawError(out var rawError))
                {
                    _logger.LogError("Subscription creation error: {StatusCode} {Body}", rawError.StatusCode, rawError.ReadAsString());
                    throw new InvalidOperationException($"Failed to create subscription: {rawError.ReadAsString()}", subEx);
                }
                throw;
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription for user {UserEmail}", userEmail);
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userEmail, CancellationToken ct = default)
    {
        try
        {
            // Find customer by email
            var customerResponse = await _client.Customers.ReadCustomerByReference(userEmail, ct: ct);
            var customerId = customerResponse.Customer?.Id ?? throw new InvalidOperationException("Customer ID not found in response");

            // Get customer subscriptions
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);

            var result = new List<SubscriptionDto>();
            foreach (var subscriptionResponse in subscriptions)
            {
                var subscription = subscriptionResponse.Subscription;
                result.Add(new SubscriptionDto
                {
                    Id = subscription?.Id ?? 0,
                    State = subscription?.State?.Value ?? string.Empty,
                    ProductPriceInCents = subscription?.ProductPriceInCents ?? 0,
                    NextAssessmentAt = subscription?.NextAssessmentAt,
                    ActivatedAt = subscription?.ActivatedAt,
                    CreatedAt = subscription?.CreatedAt,
                    Product = subscription?.Product != null ? new SubscriptionPlanDto
                    {
                        Id = subscription.Product.Id ?? 0,
                        Name = subscription.Product.Name ?? string.Empty,
                        Handle = subscription.Product.Handle ?? string.Empty,
                        Description = subscription.Product.Description ?? string.Empty,
                        PriceInCents = subscription.Product.PriceInCents ?? 0,
                        Interval = subscription.Product.Interval ?? 1,
                        IntervalUnit = subscription.Product.IntervalUnit?.Value ?? "month"
                    } : null
                });
            }

            return result;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return new List<SubscriptionDto>();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing subscriptions: {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException($"Failed to list subscriptions: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscriptions for user {UserEmail}", userEmail);
            throw;
        }
    }
}
