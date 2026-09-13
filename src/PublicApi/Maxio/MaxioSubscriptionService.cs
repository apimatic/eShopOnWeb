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
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct = default)
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

        return products.Select(p => new PlanDto
        {
            Id = p.Product?.Id,
            Name = p.Product?.Name,
            Handle = p.Product?.Handle,
            Description = p.Product?.Description,
            PriceInCents = p.Product?.PriceInCents,
            Interval = p.Product?.Interval,
            IntervalUnit = p.Product?.IntervalUnit?.Value,
            ProductFamilyHandle = p.Product?.ProductFamily?.Handle
        }).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userReference, string productHandle, CancellationToken ct = default)
    {
        var customer = await FindOrCreateCustomerAsync(userReference, ct);

        try
        {
            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerReference = userReference
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(request, ct);
            var sub = response.Subscription;

            return MapSubscription(sub);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var errors = errorList.Errors ?? Enumerable.Empty<string>();
                _logger.LogWarning("CreateSubscription validation error: {Errors}", string.Join("; ", errors));
                throw new MaxioSubscriptionException(
                    $"Subscription creation failed: {string.Join("; ", errors)}",
                    HttpStatusCode.BadRequest);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogWarning("CreateSubscription error: {StatusCode} {Body}", raw.StatusCode, raw.ReadAsString());
                throw new MaxioSubscriptionException(
                    $"Subscription creation failed with status {(int)raw.StatusCode}",
                    raw.StatusCode);
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        var customer = await FindCustomerAsync(userReference, ct);
        if (customer == null)
            return Array.Empty<SubscriptionDto>();

        var customerId = customer.Id ?? throw new InvalidOperationException("Customer ID is null");

        var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct);

        return subscriptions.Select(s => MapSubscription(s.Subscription)).ToList();
    }

    private async Task<Customer?> FindOrCreateCustomerAsync(string userReference, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(userReference, ct);
        if (existing != null)
            return existing;

        try
        {
            var request = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = "eShop",
                    LastName = "Customer",
                    Email = $"{userReference}@eshop.local",
                    Reference = userReference
                }
            };

            var response = await _client.Customers.CreateCustomer(request, ct);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
            {
                _logger.LogWarning("Customer creation returned 422 (may already exist): {Errors}", errorResponse);
                return await FindCustomerAsync(userReference, ct);
            }
            throw;
        }
    }

    private async Task<Customer?> FindCustomerAsync(string userReference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(userReference, ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static SubscriptionDto MapSubscription(Subscription? sub)
    {
        if (sub == null)
            return new SubscriptionDto();

        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State?.Value,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            ProductName = sub.Product?.Name,
            ProductHandle = sub.Product?.Handle,
            ProductPriceInCents = sub.ProductPriceInCents,
            BalanceInCents = sub.BalanceInCents
        };
    }
}

public class MaxioSubscriptionException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioSubscriptionException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
