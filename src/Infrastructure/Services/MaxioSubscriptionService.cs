using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Api;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioSubscriptionService
{
    Task<SubscriptionPlanDto[]> ListPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> SubscribeAsync(string userId, string planHandle, CancellationToken ct = default);
    Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IRepository<UserMaxioCustomer> _userCustomerRepository;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IRepository<UserMaxioCustomer> userCustomerRepository,
        string productFamilyHandle)
    {
        _client = client;
        _userCustomerRepository = userCustomerRepository;
        _productFamilyHandle = productFamilyHandle;
    }

    public async Task<SubscriptionPlanDto[]> ListPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                _productFamilyHandle,
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct);

            return products
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Product?.Id ?? 0,
                    Handle = p.Product?.Handle ?? string.Empty,
                    Name = p.Product?.Name ?? string.Empty,
                    Description = p.Product?.Description,
                    PriceInCents = p.Product?.PriceInCents ?? 0,
                    Interval = p.Product?.Interval ?? 0,
                    IntervalUnit = p.Product?.IntervalUnit?.ToString() ?? "month"
                })
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to list products: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse product list response", ex);
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string planHandle, CancellationToken ct = default)
    {
        try
        {
            var customer = await GetOrCreateCustomerAsync(userId, ct);

            var subscription = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customer.Id,
                    ProductHandle = planHandle,
                    Reference = userId,
                    PaymentCollectionMethod = CollectionMethod.Automatic
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(subscription, ct: ct);

            return MapSubscriptionFromResponse(response);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validationError))
            {
                var messages = validationError.Errors != null
                    ? string.Join(", ", validationError.Errors)
                    : "Unknown validation error";
                throw new InvalidOperationException($"Subscription validation failed: {messages}", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                throw new InvalidOperationException(
                    $"Failed to create subscription: HTTP {(int)rawError.StatusCode}", ex);
            }
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to create subscription: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse subscription response", ex);
        }
    }

    public async Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            // Find the user's Maxio customer ID from the database
            var userCustomers = await _userCustomerRepository.ListAsync(ct);
            var userCustomer = userCustomers.FirstOrDefault(u => u.UserId == userId);

            if (userCustomer == null)
            {
                return [];
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                userCustomer.MaxioCustomerId, ct: ct);

            return subscriptions
                .Select(MapSubscriptionFromResponse)
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to list subscriptions: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse subscriptions response", ex);
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            // Try to find existing customer
            var existing = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
            return existing.Customer!;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Customer doesn't exist, create a new one
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "Customer",
                    LastName = userId,
                    Email = $"{userId}@eshop.local",
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(createRequest, ct: ct);
            var customer = response.Customer!;

            // Store the mapping for future lookups
            var userCustomer = new UserMaxioCustomer
            {
                UserId = userId,
                MaxioCustomerId = customer.Id ?? 0
            };

            await _userCustomerRepository.AddAsync(userCustomer, ct);

            return customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var error))
            {
                throw new InvalidOperationException(
                    $"Customer creation validation failed: {error.Errors}", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                throw new InvalidOperationException(
                    $"Failed to create customer: HTTP {(int)rawError.StatusCode}", ex);
            }
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to lookup or create customer: HTTP {(int)ex.Error.StatusCode}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse customer response", ex);
        }
    }

    private static SubscriptionDto MapSubscriptionFromResponse(SubscriptionResponse response)
    {
        var subscription = response?.Subscription;
        return new SubscriptionDto
        {
            Id = subscription?.Id ?? 0,
            CustomerId = subscription?.Customer?.Id ?? 0,
            ProductId = subscription?.Product?.Id ?? 0,
            State = subscription?.State?.ToString() ?? "unknown",
            ActivatedAt = subscription?.ActivatedAt,
            CurrentPeriodEndsAt = subscription?.CurrentPeriodEndsAt,
            CreatedAt = subscription?.CreatedAt
        };
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public required string Handle { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public required string IntervalUnit { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public required string State { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
