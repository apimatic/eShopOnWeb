using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioSubscriptionService
{
    Task<SubscriptionPlanDto[]> ListPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> SubscribeAsync(string userId, string userEmail, string userName, string planHandle, CancellationToken ct = default);
    Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userId, string userEmail, CancellationToken ct = default);
}

public class SubscriptionPlanDto
{
    public required string Handle { get; set; }
    public required string Name { get; set; }
    public required decimal PriceInCents { get; set; }
    public required string BillingFrequency { get; set; }
    public string? Description { get; set; }
}

public class SubscriptionDto
{
    public required int Id { get; set; }
    public required string State { get; set; }
    public required string PlanHandle { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}

public class MaxioSubscriptionException : Exception
{
    public int? StatusCode { get; }

    public MaxioSubscriptionException(string message) : base(message) { }
    public MaxioSubscriptionException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly Dictionary<string, string> _userPlanHandles = new();

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, MaxioOptions options)
    {
        _client = client;
        _options = options;
    }

    public async Task<SubscriptionPlanDto[]> ListPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _options.ProductFamilyHandle,
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

            return response
                .Where(p => p.Product != null)
                .Select(p => new SubscriptionPlanDto
                {
                    Handle = p.Product!.Handle ?? "unknown",
                    Name = p.Product.Name ?? "Unknown Plan",
                    PriceInCents = p.Product.PriceInCents ?? 0,
                    BillingFrequency = GetBillingFrequency(p.Product.IntervalUnit),
                    Description = p.Product.Description
                })
                .ToArray();
        }
        catch (SdkException<ListProductsForProductFamilyError>)
        {
            throw new MaxioSubscriptionException("Failed to list subscription plans");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioSubscriptionException("Maxio service is currently unavailable");
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        string userId, string userEmail, string userName, string planHandle, CancellationToken ct = default)
    {
        var customer = await GetOrCreateCustomerAsync(userId, userEmail, userName, ct);
        var subscriptionRef = $"{userId}:{planHandle}";

        var existingSubscription = await FindExistingSubscriptionAsync(subscriptionRef, ct);
        if (existingSubscription != null)
        {
            return MapSubscriptionResponse(existingSubscription, planHandle);
        }

        try
        {
            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customer.Id,
                    ProductHandle = planHandle,
                    Reference = subscriptionRef,
                    PaymentCollectionMethod = CollectionMethod.Automatic
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: subscriptionRequest,
                ct: ct);

            if (response?.Subscription != null)
            {
                _userPlanHandles[subscriptionRef] = planHandle;
                return MapSubscriptionResponse(response.Subscription, planHandle);
            }

            throw new MaxioSubscriptionException("Invalid subscription response");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw HandleCreateSubscriptionError(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioSubscriptionException("Maxio service is currently unavailable");
        }
    }

    public async Task<SubscriptionDto[]> GetUserSubscriptionsAsync(
        string userId, string userEmail, CancellationToken ct = default)
    {
        var customer = await FindCustomerAsync(userId, ct);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: (int)customer.Id!,
                ct: ct);

            return subscriptions
                .Where(s => s?.Subscription != null)
                .Select(s => MapSubscriptionResponse(s!.Subscription!, ExtractPlanHandleFromSubscription(s.Subscription!)))
                .ToArray();
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioSubscriptionException(
                "Failed to retrieve user subscriptions",
                (int)ex.Error.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioSubscriptionException("Maxio service is currently unavailable");
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(
        string userId, string userEmail, string userName, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(userId, ct);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            var parts = userName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var firstName = parts.Length > 0 ? parts[0] : "User";
            var lastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "Account";

            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = userEmail,
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: createRequest,
                ct: ct);

            return response?.Customer ?? throw new MaxioSubscriptionException("Failed to create customer");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new MaxioSubscriptionException("Failed to create customer: validation error");
            }
            throw new MaxioSubscriptionException("Failed to create customer");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioSubscriptionException("Maxio service is currently unavailable");
        }
    }

    private async Task<Customer?> FindCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                reference: userId,
                ct: ct);

            return response?.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw new MaxioSubscriptionException(
                "Failed to look up customer",
                (int)ex.Error.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioSubscriptionException("Maxio service is currently unavailable");
        }
    }

    private async Task<Subscription?> FindExistingSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                reference: reference,
                ct: ct);

            return response?.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private SubscriptionDto MapSubscriptionResponse(Subscription? subscription, string planHandle)
    {
        if (subscription == null)
            throw new MaxioSubscriptionException("Subscription response is missing data");

        return new SubscriptionDto
        {
            Id = (int)(subscription.Id ?? 0),
            State = subscription.State?.Value ?? "unknown",
            PlanHandle = planHandle ?? subscription.Product?.Handle ?? "unknown",
            NextBillingAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt
        };
    }

    private string ExtractPlanHandleFromSubscription(Subscription subscription)
    {
        if (subscription.Reference != null && subscription.Reference.Contains(":"))
        {
            var parts = subscription.Reference.Split(':');
            if (parts.Length == 2)
                return parts[1];
        }

        if (subscription.Product?.Handle != null)
            return subscription.Product.Handle;

        _userPlanHandles.TryGetValue(subscription.Reference ?? "", out var handle);
        return handle ?? "unknown";
    }

    private static string GetBillingFrequency(IntervalUnit? intervalUnit)
    {
        if (intervalUnit == IntervalUnit.Month) return "monthly";
        if (intervalUnit == IntervalUnit.Day) return "daily";
        return "monthly";
    }

    private MaxioSubscriptionException HandleCreateSubscriptionError(
        SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var validationError))
        {
            var errorMsg = string.Join("; ", validationError.Errors ?? Array.Empty<string>());
            return new MaxioSubscriptionException($"Failed to create subscription: {errorMsg}");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return new MaxioSubscriptionException(
                "Failed to create subscription",
                (int)raw.StatusCode);
        }

        return new MaxioSubscriptionException("Failed to create subscription");
    }
}
