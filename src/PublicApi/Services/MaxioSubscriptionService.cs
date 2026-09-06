using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.Extensions.Logging;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<SubscriptionResultDto> SubscribeAsync(string userId, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IOptions<MaxioSettings> settings, ILogger<MaxioSubscriptionService> logger)
    {
        _logger = logger;

        var opts = settings.Value;
        if (string.IsNullOrEmpty(opts.ApiKey) || string.IsNullOrEmpty(opts.Subdomain))
            throw new InvalidOperationException("Maxio API credentials not configured");

        var clientOpts = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = opts.ApiKey,
                Password = "x"
            },
            Environment = ServerEnvironment.Us
        };

        if (!string.IsNullOrEmpty(opts.BaseUrl))
        {
            clientOpts.Server.Production.Us.BaseUrl = opts.BaseUrl;
        }

        var httpClient = new HttpClient();
        _client = new MaxioAdvancedBillingClient(httpClient, clientOpts);
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        var plans = new List<SubscriptionPlanDto>();

        foreach (var handle in new[] { "eshop-pro", "basic-plan" })
        {
            try
            {
                var response = await _client.Products.ReadProductByHandle(handle, ct: ct);
                if (response.Product != null)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Handle = response.Product.Handle,
                        Name = response.Product.Name,
                        PriceInCents = response.Product.PriceInCents ?? 0,
                        Interval = response.Product.Interval ?? 1,
                        IntervalUnit = response.Product.IntervalUnit?.ToString() ?? "month"
                    });
                }
            }
            catch (SdkException<RawError> ex)
            {
                _logger.LogWarning($"Failed to fetch product {handle}: {ex.Error.StatusCode}");
            }
        }

        return plans;
    }

    public async Task<SubscriptionResultDto> SubscribeAsync(
        string userId, string email, string firstName, string lastName,
        string productHandle, CancellationToken ct = default)
    {
        int customerId;

        try
        {
            var customerResponse = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
            if (customerResponse.Customer?.Id.HasValue == true)
            {
                customerId = (int)customerResponse.Customer.Id.Value;
                _logger.LogInformation($"Found existing Maxio customer {customerId} for user {userId}");
            }
            else
            {
                throw new InvalidOperationException("Customer lookup returned null ID");
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation($"Customer not found for reference {userId}, creating new customer");

            var createCustomerBody = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId
                }
            };

            try
            {
                var createResponse = await _client.Customers.CreateCustomer(createCustomerBody, ct: ct);
                if (createResponse.Customer?.Id.HasValue != true)
                    throw new InvalidOperationException("Customer creation returned null ID");

                customerId = (int)createResponse.Customer.Id.Value;
                _logger.LogInformation($"Created new Maxio customer {customerId} for user {userId}");
            }
            catch (SdkException<CreateCustomerError> createEx)
            {
                if (createEx.Error.TryGetCustomerErrorResponse1(out var errorResp))
                {
                    throw new InvalidOperationException($"Customer creation failed: {errorResp}", createEx);
                }
                else if (createEx.Error.TryGetRawError(out var rawError))
                {
                    throw new InvalidOperationException(
                        $"Customer creation failed ({(int)rawError.StatusCode}): {rawError.ReadAsString()}",
                        createEx);
                }
                throw;
            }
        }

        var createSubBody = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                Reference = $"{userId}_{productHandle}_{DateTimeOffset.UtcNow.Ticks}"
            }
        };

        try
        {
            var subResponse = await _client.Subscriptions.CreateSubscription(createSubBody, ct: ct);
            if (subResponse.Subscription == null)
                throw new InvalidOperationException("Subscription creation returned null subscription");

            return new SubscriptionResultDto
            {
                SubscriptionId = subResponse.Subscription.Id ?? 0,
                State = subResponse.Subscription.State?.ToString() ?? "unknown",
                ProductHandle = productHandle,
                PriceInCents = subResponse.Subscription.ProductPriceInCents ?? 0,
                CurrentPeriodStartsAt = subResponse.Subscription.CurrentPeriodStartedAt,
                CurrentPeriodEndsAt = subResponse.Subscription.CurrentPeriodEndsAt,
                NextBillingAt = subResponse.Subscription.NextAssessmentAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var errors = string.Join(", ", errorList.Errors ?? new List<string>());
                throw new InvalidOperationException($"Subscription creation failed: {errors}", ex);
            }
            else if (ex.Error.TryGetRawError(out var rawError))
            {
                throw new InvalidOperationException(
                    $"Subscription creation failed ({(int)rawError.StatusCode}): {rawError.ReadAsString()}",
                    ex);
            }
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        var subscriptions = new List<SubscriptionDto>();

        try
        {
            var customerResponse = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
            if (customerResponse.Customer?.Id.HasValue != true)
            {
                _logger.LogInformation($"No customer found for reference {userId}");
                return subscriptions;
            }

            var customerId = (int)customerResponse.Customer.Id.Value;
            var subsList = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);

            foreach (var item in subsList)
            {
                if (item.Subscription != null)
                {
                    subscriptions.Add(new SubscriptionDto
                    {
                        Id = item.Subscription.Id ?? 0,
                        State = item.Subscription.State?.ToString() ?? "unknown",
                        PriceInCents = item.Subscription.ProductPriceInCents ?? 0,
                        CurrentPeriodStartsAt = item.Subscription.CurrentPeriodStartedAt,
                        CurrentPeriodEndsAt = item.Subscription.CurrentPeriodEndsAt,
                        NextBillingAt = item.Subscription.NextAssessmentAt
                    });
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Failed to list subscriptions for user {userId}: {ex.Error.StatusCode}");
            throw new InvalidOperationException($"Failed to retrieve subscriptions: {ex.Error.StatusCode}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError($"Failed to deserialize subscription response: {ex.Message}");
            throw new InvalidOperationException("Failed to process subscription response", ex);
        }

        return subscriptions;
    }
}

public class SubscriptionPlanDto
{
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionResultDto
{
    public long SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public long PriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}

public class SubscriptionDto
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long PriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}
