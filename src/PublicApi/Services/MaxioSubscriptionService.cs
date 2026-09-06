using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Services;

public sealed class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly CatalogContext _catalogContext;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        string productFamilyHandle,
        CatalogContext catalogContext,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _productFamilyHandle = productFamilyHandle;
        _catalogContext = catalogContext;
        _logger = logger;
    }

    public async Task<IEnumerable<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _productFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: ct);

            return response.Select(pr => new SubscriptionPlanDto
            {
                Handle = pr.Product.Handle,
                Name = pr.Product.Name,
                Description = pr.Product.Description,
                PriceInCents = (long)(pr.Product.DefaultProductPricePointId ?? 0)
            }).ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFoundMsg))
            {
                _logger.LogError("Product family not found: {Message}", notFoundMsg);
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError("Failed to list plans: HTTP {Status}", (int)raw.StatusCode);
            }
            throw;
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        string userId,
        string productHandle,
        CancellationToken ct)
    {
        var customerId = await EnsureCustomerExistsAsync(userId, ct);

        try
        {
            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    Reference = userId
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: request,
                ct: ct);

            var subscription = response.Subscription
                ?? throw new InvalidOperationException("Subscription response missing subscription data");

            var localSubscription = new Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription
            {
                UserId = userId,
                MaxioSubscriptionId = subscription.Id ?? 0,
                MaxioCustomerId = customerId,
                ProductHandle = productHandle,
                PriceInCents = subscription.ProductPriceInCents ?? 0,
                Status = subscription.State ?? "unknown",
                CurrentPeriodStartsAt = subscription.CurrentPeriodStartedAt?.DateTime,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt?.DateTime,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _catalogContext.Subscriptions.Add(localSubscription);
            await _catalogContext.SaveChangesAsync(ct);

            return new SubscriptionDto
            {
                Id = localSubscription.Id,
                MaxioSubscriptionId = subscription.Id ?? 0,
                ProductHandle = productHandle,
                Status = subscription.State ?? "unknown",
                PriceInCents = subscription.ProductPriceInCents ?? 0,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt?.DateTime
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out ErrorListResponse1 errors))
            {
                _logger.LogError("Subscription validation failed: {Errors}",
                    string.Join(", ", errors.Errors ?? []));
                throw new InvalidOperationException(
                    $"Subscription creation failed: {string.Join(", ", errors.Errors ?? [])}");
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError("Subscription creation failed: HTTP {Status}", (int)raw.StatusCode);
            }
            throw;
        }
    }

    public async Task<IEnumerable<SubscriptionDto>> ListCustomerSubscriptionsAsync(
        string userId,
        CancellationToken ct)
    {
        var localSubscriptions = _catalogContext.Subscriptions
            .Where(s => s.UserId == userId)
            .ToList();

        if (!localSubscriptions.Any())
        {
            return [];
        }

        var customerId = localSubscriptions.First().MaxioCustomerId;

        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: ct);

            var subscriptionMap = response
                .Where(sr => sr.Subscription != null)
                .ToDictionary(sr => sr.Subscription!.Id ?? 0);

            return localSubscriptions
                .Where(s => subscriptionMap.ContainsKey(s.MaxioSubscriptionId))
                .Select(s =>
                {
                    var maxioSub = subscriptionMap[s.MaxioSubscriptionId].Subscription!;
                    return new SubscriptionDto
                    {
                        Id = s.Id,
                        MaxioSubscriptionId = s.MaxioSubscriptionId,
                        ProductHandle = s.ProductHandle,
                        Status = maxioSub.State ?? s.Status,
                        PriceInCents = (long?)(maxioSub.BalanceInCents ?? s.PriceInCents) ?? 0,
                        CurrentPeriodEndsAt = maxioSub.CurrentPeriodEndsAt?.DateTime ?? s.CurrentPeriodEndsAt
                    };
                })
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Failed to list subscriptions: HTTP {Status}", (int)ex.Error.StatusCode);
            throw;
        }
    }

    private async Task<int> EnsureCustomerExistsAsync(string userId, CancellationToken ct)
    {
        try
        {
            var existingCustomer = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);

            return existingCustomer.Customer?.Id ?? throw new InvalidOperationException(
                "Customer lookup returned empty response");
        }
        catch (SdkException<RawError> ex)
        {
            if ((int)ex.Error.StatusCode == 404)
            {
                return await CreateCustomerAsync(userId, ct);
            }
            _logger.LogError("Failed to lookup customer: HTTP {Status}", (int)ex.Error.StatusCode);
            throw;
        }
    }

    private async Task<int> CreateCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            var request = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "Customer",
                    LastName = userId,
                    Email = $"{userId}@example.com",
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: request,
                ct: ct);

            return response.Customer?.Id ?? throw new InvalidOperationException(
                "Customer creation returned empty response");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out CustomerErrorResponse1 errors))
            {
                _logger.LogError("Customer creation validation failed: {Errors}",
                    string.Join(", ", errors.Errors?.PerPage ?? []));
                throw new InvalidOperationException(
                    $"Customer creation failed: {string.Join(", ", errors.Errors?.PerPage ?? [])}");
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                _logger.LogError("Customer creation failed: HTTP {Status}", (int)raw.StatusCode);
            }
            throw;
        }
    }
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
}

public sealed class SubscriptionDto
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string Status { get; set; } = null!;
    public long PriceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}
