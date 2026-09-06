using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<ProductDto>> GetAvailablePlansAsync(CancellationToken ct);
    Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, string planHandle, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct);
}

public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? Handle { get; set; }
    public string? State { get; set; }
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }
    public decimal? PriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

public class ProductDto
{
    public int? Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _logger = logger;
        var opts = options.Value;

        var credentials = new BasicAuthCredentials
        {
            Username = opts.ApiKey,
            Password = "x"
        };

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = credentials,
            Environment = ServerEnvironment.Us
        };

        // Configure server/site settings
        if (!string.IsNullOrEmpty(opts.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = opts.BaseUrl;
        }
        clientOptions.Server.Production.Us.Site = opts.Subdomain;

        _client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    public async Task<IReadOnlyList<ProductDto>> GetAvailablePlansAsync(CancellationToken ct)
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
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 100,
                ct: ct);

            return response
                .Where(p => p.Product != null)
                .Select(p => new ProductDto
                {
                    Id = p.Product!.Id,
                    Handle = p.Product!.Handle,
                    Name = p.Product!.Name,
                    PriceInCents = p.Product!.PriceInCents,
                    Interval = p.Product!.Interval,
                    IntervalUnit = p.Product!.IntervalUnit?.Value
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to fetch subscription plans: {ex.Message}");
            throw;
        }
    }

    public async Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, string planHandle, CancellationToken ct)
    {
        try
        {
            // Step 1: Look up or create customer
            int customerId = await EnsureCustomerExistsAsync(userId, ct);

            // Step 2: Check for existing subscription to this plan
            var existingSubs = await GetUserSubscriptionsInternalAsync(customerId, ct);
            if (existingSubs.Any(s => s.Handle == planHandle && s.State != "canceled" && s.State != "expired"))
            {
                _logger.LogWarning($"User {userId} already has active subscription to plan {planHandle}");
                throw new InvalidOperationException($"User already has an active subscription to {planHandle}");
            }

            // Step 3: Create subscription
            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerReference = userId,
                    Reference = $"{userId}_{planHandle}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(createRequest, ct);

            if (response.Subscription == null)
            {
                _logger.LogWarning("Subscription creation returned null Subscription field");
                throw new InvalidOperationException("Failed to create subscription: null response");
            }

            return MapSubscriptionDto(response.Subscription);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Subscription creation failed: {ex.Message}");
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct)
    {
        try
        {
            int customerId = await GetCustomerIdAsync(userId, ct);
            if (customerId == 0)
            {
                return new List<SubscriptionDto>();
            }

            return await GetUserSubscriptionsInternalAsync(customerId, ct);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return new List<SubscriptionDto>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to retrieve subscriptions: {ex.Message}");
            throw;
        }
    }

    private async Task<IReadOnlyList<SubscriptionDto>> GetUserSubscriptionsInternalAsync(int customerId, CancellationToken ct)
    {
        var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct);

        return response
            .Where(s => s.Subscription != null)
            .Select(s => MapSubscriptionDto(s.Subscription!))
            .ToList();
    }

    private async Task<int> EnsureCustomerExistsAsync(string userId, CancellationToken ct)
    {
        var existingId = await GetCustomerIdAsync(userId, ct);
        if (existingId > 0)
        {
            return existingId;
        }

        // Create customer
        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                Reference = userId,
                Email = userId,
                FirstName = "eShop",
                LastName = "Customer"
            }
        };

        var response = await _client.Customers.CreateCustomer(createRequest, ct);

        if (response.Customer?.Id is null or <= 0)
        {
            _logger.LogWarning("Customer creation returned invalid ID");
            throw new InvalidOperationException("Failed to create customer: invalid response");
        }

        return response.Customer.Id.Value;
    }

    private async Task<int> GetCustomerIdAsync(string userId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(userId, ct);
            return response.Customer?.Id ?? 0;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return 0;
        }
    }

    private static SubscriptionDto MapSubscriptionDto(Subscription sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            Handle = sub.Product?.Handle,
            State = sub.State?.Value,
            ProductId = sub.Product?.Id,
            ProductName = sub.Product?.Name,
            PriceInCents = sub.Product?.PriceInCents,
            NextBillingDate = sub.NextAssessmentAt,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt
        };
    }
}
