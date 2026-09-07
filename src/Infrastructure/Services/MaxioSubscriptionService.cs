using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly CatalogContext _context;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        CatalogContext context,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _context = context;
        _logger = logger;
    }

    public async Task<int> EnsureCustomerAsync(string userId, string email, CancellationToken ct = default)
    {
        var spec = new MaxioCustomerByUserIdSpec(userId);
        var existing = await _context.MaxioCustomers.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken: ct);

        if (existing != null)
        {
            return existing.MaxioCustomerId;
        }

        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = userId,
                    LastName = string.Empty,
                    Email = email,
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(createRequest, ct: ct);
            var customer = response.Customer;

            if (customer?.Id == null)
            {
                throw new InvalidOperationException("Customer creation response missing ID");
            }

            var maxioCustomer = new MaxioCustomer
            {
                UserId = userId,
                MaxioCustomerId = customer.Id.Value,
                Email = email,
                CreatedAt = customer.CreatedAt ?? DateTimeOffset.UtcNow,
                UpdatedAt = customer.UpdatedAt ?? DateTimeOffset.UtcNow
            };

            _context.MaxioCustomers.Add(maxioCustomer);
            await _context.SaveChangesAsync(ct);
            return customer.Id.Value;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Try to get the detailed error response
            if (ex.Error.TryGetCustomerErrorResponse1(out var errResp))
            {
                // Collect error messages from available fields
                var errors = new List<string>();
                if (errResp?.Errors?.PerPage != null)
                {
                    errors.AddRange(errResp.Errors.PerPage);
                }
                if (errResp?.Errors?.PricePoint != null)
                {
                    errors.AddRange(errResp.Errors.PricePoint);
                }

                var errorMsg = string.Join("; ", errors);
                if (errorMsg.Contains("has already been taken", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("already been taken", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation($"Customer {email} already exists in Maxio");
                    throw new InvalidOperationException($"Customer {email} already exists", ex);
                }
            }

            // Fall back to raw error if typed error doesn't have the info we need
            if (ex.Error.TryGetRawError(out var rawError))
            {
                var rawErrorMsg = rawError.ReadAsString();
                if (rawErrorMsg.Contains("has already been taken", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation($"Customer {email} already exists in Maxio");
                    throw new InvalidOperationException($"Customer {email} already exists", ex);
                }
            }

            throw;
        }
    }

    public async Task<List<ProductDto>> FetchPlansAsync(CancellationToken ct = default)
    {
        var products = await _client.Products.ListProducts(
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

        var result = new List<ProductDto>();

        foreach (var productResponse in products)
        {
            var product = productResponse.Product;
            if (product?.Id != null)
            {
                result.Add(new ProductDto
                {
                    Id = product.Id.Value,
                    Handle = product.Handle ?? string.Empty,
                    Name = product.Name ?? string.Empty,
                    Description = product.Description ?? string.Empty,
                    PriceInCents = product.PriceInCents ?? 0,
                    Interval = product.Interval ?? 0,
                    IntervalUnit = product.IntervalUnit?.ToString() ?? string.Empty
                });
            }
        }

        return result;
    }

    public async Task<SubscriptionDataDto> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default)
    {
        var createRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(createRequest, ct: ct);
            var subscription = response.Subscription;

            if (subscription?.Id == null)
            {
                throw new InvalidOperationException("Subscription creation response missing ID");
            }

            return MapSubscriptionToDto(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errList) && errList?.Errors != null)
            {
                var errorMsg = string.Join("; ", errList.Errors);
                _logger.LogWarning($"Subscription creation failed: {errorMsg}");
            }

            throw;
        }
    }

    public async Task<SubscriptionDataDto> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default)
    {
        var response = await _client.Subscriptions.ReadSubscription(
            subscriptionId: subscriptionId,
            include: null,
            ct: ct);

        var subscription = response.Subscription;
        if (subscription == null)
        {
            throw new InvalidOperationException("Subscription not found");
        }

        return MapSubscriptionToDto(subscription);
    }

    public async Task<List<SubscriptionDataDto>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);

        var result = new List<SubscriptionDataDto>();
        foreach (var subResponse in subscriptions)
        {
            var sub = subResponse.Subscription;
            if (sub != null)
            {
                result.Add(MapSubscriptionToDto(sub));
            }
        }

        return result;
    }

    private static SubscriptionDataDto MapSubscriptionToDto(Subscription subscription)
    {
        return new SubscriptionDataDto
        {
            Id = subscription.Id ?? 0,
            CustomerId = subscription.Customer?.Id ?? 0,
            ProductId = subscription.Product?.Id ?? 0,
            State = subscription.State?.ToString() ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? 0,
            NextBillingAt = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = subscription.UpdatedAt ?? DateTimeOffset.UtcNow
        };
    }
}
