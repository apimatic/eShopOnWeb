using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IRepository<Entities.SubscriptionAggregate.MaxioCustomer> _maxioCustomerRepo;
    private readonly IRepository<Entities.SubscriptionAggregate.UserSubscription> _userSubscriptionRepo;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IRepository<Entities.SubscriptionAggregate.MaxioCustomer> maxioCustomerRepo,
        IRepository<Entities.SubscriptionAggregate.UserSubscription> userSubscriptionRepo,
        string productFamilyHandle)
    {
        _client = client;
        _maxioCustomerRepo = maxioCustomerRepo;
        _userSubscriptionRepo = userSubscriptionRepo;
        _productFamilyHandle = productFamilyHandle;
    }

    public async Task<SubscriptionPlanDto[]> ListSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
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
                perPage: 20,
                ct: ct);

            return products
                .Where(p => p != null)
                .Select(p => new SubscriptionPlanDto
                {
                    Handle = ExtractProductHandle(p),
                    Name = ExtractProductName(p),
                    PriceUSD = ExtractProductPrice(p),
                    Interval = ExtractProductInterval(p),
                    IntervalUnit = ExtractProductIntervalUnit(p)
                })
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to list subscription plans: HTTP {(int)ex.Error.StatusCode}",
                ex);
        }
    }

    public async Task<SubscriptionDto> CreateOrUpdateSubscriptionAsync(
        string userId,
        string userEmail,
        string userFirstName,
        string userLastName,
        string planHandle,
        CancellationToken ct = default)
    {
        var maxioCustomerId = await EnsureMaxioCustomerAsync(userId, userEmail, userFirstName, userLastName, ct);

        try
        {
            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerReference = userId,
                    ProductHandle = planHandle,
                    PaymentCollectionMethod = CollectionMethod.Automatic
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: subscriptionRequest,
                ct: ct);

            var subscription = response.Subscription;
            var subscriptionDto = BuildSubscriptionDto(subscription);

            var userSubscription = new Entities.SubscriptionAggregate.UserSubscription
            {
                UserId = userId,
                MaxioSubscriptionId = subscriptionDto.Id,
                MaxioCustomerReference = userId,
                PlanHandle = subscriptionDto.PlanHandle,
                PlanName = subscriptionDto.PlanName,
                PriceInCents = (long)(subscriptionDto.PriceUSD * 100),
                State = subscriptionDto.State,
                NextBillingDate = subscriptionDto.NextBillingDate,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var existing = (await _userSubscriptionRepo.ListAsync()).FirstOrDefault(
                s => s.UserId == userId && s.MaxioSubscriptionId == subscriptionDto.Id);

            if (existing != null)
            {
                existing.PlanHandle = userSubscription.PlanHandle;
                existing.PlanName = userSubscription.PlanName;
                existing.PriceInCents = userSubscription.PriceInCents;
                existing.State = userSubscription.State;
                existing.NextBillingDate = userSubscription.NextBillingDate;
                existing.UpdatedAt = DateTime.UtcNow;
                await _userSubscriptionRepo.UpdateAsync(existing);
            }
            else
            {
                await _userSubscriptionRepo.AddAsync(userSubscription);
            }

            return subscriptionDto;
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create subscription: HTTP {(int)ex.Error.StatusCode}",
                ex);
        }
    }

    public async Task<SubscriptionDto[]> ListUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        var maxioCustomer = (await _maxioCustomerRepo.ListAsync()).FirstOrDefault(c => c.UserId == userId);
        if (maxioCustomer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: maxioCustomer.MaxioCustomerId,
                ct: ct);

            return subscriptions
                .Where(s => s != null)
                .Select(BuildSubscriptionDto)
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            if ((int)ex.Error.StatusCode == 404)
            {
                return Array.Empty<SubscriptionDto>();
            }
            throw new InvalidOperationException(
                $"Failed to list user subscriptions: HTTP {(int)ex.Error.StatusCode}",
                ex);
        }
    }

    private async Task<int> EnsureMaxioCustomerAsync(
        string userId,
        string userEmail,
        string userFirstName,
        string userLastName,
        CancellationToken ct)
    {
        var existingCustomer = (await _maxioCustomerRepo.ListAsync()).FirstOrDefault(c => c.UserId == userId);
        if (existingCustomer != null)
        {
            return existingCustomer.MaxioCustomerId;
        }

        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);

            var customer = response.Customer;
            var maxioCustomer = new Entities.SubscriptionAggregate.MaxioCustomer
            {
                UserId = userId,
                MaxioCustomerId = customer?.Id ?? 0,
                MaxioReference = userId,
                CreatedAt = DateTime.UtcNow
            };

            await _maxioCustomerRepo.AddAsync(maxioCustomer);
            return customer?.Id ?? 0;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = userFirstName,
                    LastName = userLastName,
                    Email = userEmail,
                    Reference = userId
                }
            };

            try
            {
                var createResponse = await _client.Customers.CreateCustomer(
                    body: createRequest,
                    ct: ct);

                var customer = createResponse.Customer;
                var maxioCustomer = new Entities.SubscriptionAggregate.MaxioCustomer
                {
                    UserId = userId,
                    MaxioCustomerId = customer?.Id ?? 0,
                    MaxioReference = userId,
                    CreatedAt = DateTime.UtcNow
                };

                await _maxioCustomerRepo.AddAsync(maxioCustomer);
                return customer?.Id ?? 0;
            }
            catch (SdkException<RawError> createEx)
            {
                throw new InvalidOperationException(
                    $"Failed to create customer: HTTP {(int)createEx.Error.StatusCode}",
                    createEx);
            }
        }
    }

    private SubscriptionDto BuildSubscriptionDto(dynamic subscription)
    {
        if (subscription == null)
            return new SubscriptionDto { Id = 0, PlanHandle = "", PlanName = "", PriceUSD = 0, State = "unknown" };

        var product = subscription.Product;
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            PlanHandle = product?.Handle?.ToString() ?? "",
            PlanName = product?.Name?.ToString() ?? "",
            PriceUSD = (subscription.ProductPriceInCents ?? 0) / 100.0m,
            State = subscription.State?.Value?.ToString() ?? "unknown",
            NextBillingDate = subscription.NextAssessmentAt
        };
    }

    private string ExtractProductHandle(dynamic product) => product?.Handle?.ToString() ?? "";
    private string ExtractProductName(dynamic product) => product?.Name?.ToString() ?? "";
    private decimal ExtractProductPrice(dynamic product) => ((product?.PriceInCents ?? 0) as long? ?? 0) / 100.0m;
    private int ExtractProductInterval(dynamic product) => (product?.Interval ?? 1) as int? ?? 1;
    private string ExtractProductIntervalUnit(dynamic product) => product?.IntervalUnit?.Value?.ToString() ?? "month";
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public decimal PriceUSD { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = null!;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = null!;
    public string PlanName { get; set; } = null!;
    public decimal PriceUSD { get; set; }
    public string State { get; set; } = null!;
    public DateTimeOffset? NextBillingDate { get; set; }
}
