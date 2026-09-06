using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    private static readonly ConcurrentDictionary<string, int> UserCustomerMap = new();
    private static readonly ConcurrentDictionary<string, List<SubscriptionInfo>> UserSubscriptionMap = new();

    private class SubscriptionInfo
    {
        public int SubscriptionId { get; set; }
        public string? PlanHandle { get; set; }
        public string? PlanName { get; set; }
        public long? PriceInCents { get; set; }
        public string? State { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
    }

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<int?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentNullException(nameof(userId));

        if (UserCustomerMap.TryGetValue(userId, out var existingCustomerId))
        {
            _logger.LogInformation("Customer already exists for user {UserId}: {CustomerId}", userId, existingCustomerId);
            return existingCustomerId;
        }

        try
        {
            var customer = await _client.Customers.ReadCustomerByReference(reference: userId, ct: default);
            if (customer?.Customer?.Id.HasValue == true)
            {
                var customerId = (int)customer.Customer.Id;
                UserCustomerMap.TryAdd(userId, customerId);
                _logger.LogInformation("Found existing Maxio customer for user {UserId}: {CustomerId}", userId, customerId);
                return customerId;
            }
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode != HttpStatusCode.NotFound)
            {
                _logger.LogError(ex, "Error looking up customer by reference for user {UserId}", userId);
                throw;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing customer response for user {UserId}", userId);
            throw;
        }

        return await CreateCustomerAsync(userId, email, firstName, lastName);
    }

    private async Task<int?> CreateCustomerAsync(string userId, string email, string firstName, string lastName)
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

            var response = await _client.Customers.CreateCustomer(body: createRequest, ct: default);
            if (response?.Customer?.Id.HasValue == true)
            {
                var customerId = (int)response.Customer.Id;
                UserCustomerMap.TryAdd(userId, customerId);
                _logger.LogInformation("Created new Maxio customer for user {UserId}: {CustomerId}", userId, customerId);
                return customerId;
            }
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            _logger.LogError(ex, "Error creating customer for user {UserId}", userId);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing create customer response for user {UserId}", userId);
            throw;
        }

        return null;
    }

    public async Task<int?> GetMaxioCustomerIdAsync(string userId)
    {
        if (UserCustomerMap.TryGetValue(userId, out var customerId))
            return customerId;

        try
        {
            var customer = await _client.Customers.ReadCustomerByReference(reference: userId, ct: default);
            if (customer?.Customer?.Id.HasValue == true)
            {
                var id = (int)customer.Customer.Id;
                UserCustomerMap.TryAdd(userId, id);
                return id;
            }
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode != HttpStatusCode.NotFound)
            {
                _logger.LogError(ex, "Error looking up customer for user {UserId}", userId);
                throw;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing customer response for user {UserId}", userId);
            throw;
        }

        return null;
    }

    public async Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, int customerId, string planHandle)
    {
        try
        {
            var createRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = planHandle,
                    Reference = $"{userId}|{planHandle}|{DateTime.UtcNow:yyyyMMddHHmmss}"
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(body: createRequest, ct: default);
            if (response?.Subscription != null)
            {
                var sub = response.Subscription;
                var subInfo = new SubscriptionInfo
                {
                    SubscriptionId = (int)(sub.Id ?? 0),
                    PlanHandle = planHandle,
                    PlanName = sub.Product?.Name,
                    PriceInCents = sub.ProductPriceInCents,
                    State = sub.State?.Value,
                    CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                    NextAssessmentAt = sub.NextAssessmentAt
                };

                var userKey = $"{userId}|subscriptions";
                UserSubscriptionMap.AddOrUpdate(userKey,
                    new List<SubscriptionInfo> { subInfo },
                    (_, list) => { list.Add(subInfo); return list; });

                _logger.LogInformation("Created subscription for user {UserId}: {SubscriptionId}", userId, subInfo.SubscriptionId);

                return new SubscriptionDto
                {
                    MaxioSubscriptionId = subInfo.SubscriptionId,
                    PlanHandle = subInfo.PlanHandle,
                    PlanName = subInfo.PlanName,
                    PriceInCents = subInfo.PriceInCents ?? 0,
                    State = subInfo.State,
                    CurrentPeriodEndsAt = subInfo.CurrentPeriodEndsAt,
                    NextAssessmentAt = subInfo.NextAssessmentAt
                };
            }
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId}", userId);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing subscription response for user {UserId}", userId);
            throw;
        }

        return null;
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        var result = new List<SubscriptionDto>();

        try
        {
            var userKey = $"{userId}|subscriptions";
            if (UserSubscriptionMap.TryGetValue(userKey, out var cachedSubs))
            {
                return cachedSubs.Select(s => new SubscriptionDto
                {
                    MaxioSubscriptionId = s.SubscriptionId,
                    PlanHandle = s.PlanHandle,
                    PlanName = s.PlanName,
                    PriceInCents = s.PriceInCents ?? 0,
                    State = s.State,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    NextAssessmentAt = s.NextAssessmentAt
                }).ToList();
            }

            var customerId = await GetMaxioCustomerIdAsync(userId);
            if (customerId == null)
            {
                _logger.LogInformation("No Maxio customer found for user {UserId}", userId);
                return result;
            }

            var subscriptions = await _client.Subscriptions.ListSubscriptions(
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
                perPage: 100,
                ct: default);

            var userSubscriptions = new List<SubscriptionInfo>();

            foreach (var subResponse in subscriptions)
            {
                if (subResponse?.Subscription != null && subResponse.Subscription.Customer?.Id == customerId)
                {
                    var sub = subResponse.Subscription;
                    var subInfo = new SubscriptionInfo
                    {
                        SubscriptionId = (int)(sub.Id ?? 0),
                        PlanHandle = sub.Product?.Handle,
                        PlanName = sub.Product?.Name,
                        PriceInCents = sub.ProductPriceInCents,
                        State = sub.State?.Value,
                        CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                        NextAssessmentAt = sub.NextAssessmentAt
                    };

                    userSubscriptions.Add(subInfo);

                    result.Add(new SubscriptionDto
                    {
                        MaxioSubscriptionId = subInfo.SubscriptionId,
                        PlanHandle = subInfo.PlanHandle,
                        PlanName = subInfo.PlanName,
                        PriceInCents = subInfo.PriceInCents ?? 0,
                        State = subInfo.State,
                        CurrentPeriodEndsAt = subInfo.CurrentPeriodEndsAt,
                        NextAssessmentAt = subInfo.NextAssessmentAt
                    });
                }
            }

            if (userSubscriptions.Count > 0)
            {
                UserSubscriptionMap.TryAdd(userKey, userSubscriptions);
            }

            _logger.LogInformation("Found {Count} subscriptions for user {UserId}", result.Count, userId);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing subscriptions for user {UserId}", userId);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing subscriptions response for user {UserId}", userId);
            throw;
        }

        return result;
    }
}
