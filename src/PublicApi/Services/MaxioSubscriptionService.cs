using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly CatalogContext _context;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient maxioClient,
        CatalogContext context,
        ILogger<MaxioSubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _context = context;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct)
    {
        try
        {
            var plans = new List<SubscriptionPlanDto>();

            var proHandle = "eshop-pro";
            var basicHandle = "basic-plan";

            foreach (var handle in new[] { proHandle, basicHandle })
            {
                try
                {
                    var response = await _maxioClient.Products.ReadProductByHandle(apiHandle: handle, ct: ct);
                    var product = response.Product;

                    if (product != null)
                    {
                        plans.Add(new SubscriptionPlanDto
                        {
                            Handle = product.Handle ?? "",
                            Name = product.Name ?? "",
                            Description = product.Description ?? "",
                            PriceInCents = product.PriceInCents ?? 0L,
                            IntervalInMonths = (int)(product.Interval ?? 1)
                        });
                    }
                }
                catch (SdkException<RawError> ex)
                {
                    _logger.LogWarning("Failed to fetch product {Handle}: {StatusCode}", handle, ex.Error.StatusCode);
                }
            }

            return plans;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error when fetching plans");
            throw new InvalidOperationException("Failed to deserialize plan data from Maxio", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error when fetching plans");
            throw new InvalidOperationException("Failed to communicate with Maxio", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userId,
        string planHandle,
        string userEmail,
        string firstName,
        string lastName,
        CancellationToken ct)
    {
        try
        {
            var existingMapping = _context.MaxioCustomerMappings
                .FirstOrDefault(m => m.UserId == userId);

            int maxioCustomerId;

            if (existingMapping != null)
            {
                maxioCustomerId = existingMapping.MaxioCustomerId;
            }
            else
            {
                var customerResponse = await _maxioClient.Customers.CreateCustomer(
                    body: new MaxioAdvancedBilling.Models.CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = userEmail,
                            Reference = userId
                        }
                    },
                    ct: ct);

                var customer = customerResponse.Customer;
                if (customer?.Id == null)
                {
                    throw new InvalidOperationException("Failed to create Maxio customer");
                }

                maxioCustomerId = (int)customer.Id;

                var mapping = new MaxioCustomerMapping
                {
                    UserId = userId,
                    MaxioCustomerId = maxioCustomerId,
                    Reference = userId,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _context.MaxioCustomerMappings.Add(mapping);
                await _context.SaveChangesAsync(ct);
            }

            var subscriptionRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = maxioCustomerId,
                    PaymentCollectionMethod = CollectionMethod.Automatic
                }
            };

            var subscriptionResponse = await _maxioClient.Subscriptions.CreateSubscription(
                body: subscriptionRequest,
                ct: ct);

            var subscription = subscriptionResponse.Subscription;
            if (subscription?.Id == null)
            {
                throw new InvalidOperationException("Failed to create subscription");
            }

            var product = subscription.Product;
            return new SubscriptionDto
            {
                Id = (int)subscription.Id,
                State = subscription.State?.Value ?? "unknown",
                ProductHandle = product?.Handle ?? "",
                ProductName = product?.Name ?? "",
                PriceInCents = subscription.ProductPriceInCents ?? 0L,
                NextBillingAt = subscription.NextAssessmentAt,
                CreatedAt = subscription.CreatedAt ?? DateTimeOffset.UtcNow
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error when creating subscription");
            throw new InvalidOperationException("Failed to process subscription response from Maxio", ex);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio API error: {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException($"Failed to create subscription: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error when creating subscription");
            throw new InvalidOperationException("Failed to communicate with Maxio", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(
        string userId,
        CancellationToken ct)
    {
        try
        {
            var subscriptions = new List<SubscriptionDto>();

            var mapping = _context.MaxioCustomerMappings
                .FirstOrDefault(m => m.UserId == userId);

            if (mapping == null)
            {
                return subscriptions;
            }

            var response = await _maxioClient.Subscriptions.ListSubscriptions(
                state: SubscriptionStateFilter.Active,
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
                perPage: 20,
                ct: ct);

            foreach (var sub in response)
            {
                if (sub.Subscription?.Id != null && sub.Subscription.Customer?.Id == mapping.MaxioCustomerId)
                {
                    var product = sub.Subscription.Product;
                    subscriptions.Add(new SubscriptionDto
                    {
                        Id = (int)sub.Subscription.Id,
                        State = sub.Subscription.State?.Value ?? "unknown",
                        ProductHandle = product?.Handle ?? "",
                        ProductName = product?.Name ?? "",
                        PriceInCents = sub.Subscription.ProductPriceInCents ?? 0L,
                        NextBillingAt = sub.Subscription.NextAssessmentAt,
                        CreatedAt = sub.Subscription.CreatedAt ?? DateTimeOffset.UtcNow
                    });
                }
            }

            return subscriptions;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error when fetching subscriptions");
            throw new InvalidOperationException("Failed to deserialize subscriptions from Maxio", ex);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio API error: {StatusCode}", ex.Error.StatusCode);
            throw new InvalidOperationException($"Failed to fetch subscriptions: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error when fetching subscriptions");
            throw new InvalidOperationException("Failed to communicate with Maxio", ex);
        }
    }
}
