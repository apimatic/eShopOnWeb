using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Backs the subscription endpoints with Maxio Advanced Billing as the system of record. All Maxio
/// lookups are keyed by handle/reference strings, never by numeric ids. Subscription creation is guarded
/// client-side (Maxio has no server-side duplicate protection), serialized per customer reference, and
/// reconciled after a transport failure, so a retried or repeated subscribe returns the existing
/// subscription instead of creating a duplicate.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> ClosedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "suspended",
        "trial_ended"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly SubscriberLock _subscriberLock;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly TimeSpan _callBudget = TimeSpan.FromSeconds(45);

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        SubscriberLock subscriberLock,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _subscriberLock = subscriberLock;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        return BoundedAsync(async token =>
        {
            var products = await ListProductsAsync(token);
            if (products.Count == 0)
            {
                return (IReadOnlyList<SubscriptionPlanDto>)Array.Empty<SubscriptionPlanDto>();
            }

            var currency = await ReadSiteCurrencyAsync(token);
            return products.Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Price = ToMoney(p.PriceInCents ?? 0),
                Currency = currency,
                Interval = p.Interval ?? 1,
                IntervalUnit = p.IntervalUnit?.Value ?? string.Empty
            }).ToList();
        }, ct);
    }

    public Task<SubscribeResult> SubscribeAsync(SubscriberProfile subscriber, string planHandle, CancellationToken ct)
    {
        return BoundedAsync(async token =>
        {
            var products = await ListProductsAsync(token);
            var product = products.FirstOrDefault(p =>
                string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (product is null)
            {
                var available = string.Join(", ",
                    products.Where(p => !string.IsNullOrWhiteSpace(p.Handle)).Select(p => p.Handle));
                throw new MaxioApiException(HttpStatusCode.BadRequest,
                    $"The plan '{planHandle}' is not available for subscription. Available plans: {available}.");
            }

            using (await _subscriberLock.AcquireAsync(subscriber.Reference, token))
            {
                var customer = await FindOrCreateCustomerAsync(subscriber, token);
                if (customer.Id is not int customerId)
                {
                    throw new MaxioApiException(HttpStatusCode.BadGateway,
                        "Maxio returned a customer without an id.");
                }

                var existing = await FindExistingSubscriptionAsync(customerId, product.Handle!, token);
                if (existing is not null)
                {
                    return new SubscribeResult { Subscription = existing, Created = false };
                }

                var nextBillingAt = ComputeNextBillingDate(product);
                var created = await CreateSubscriptionAsync(product.Handle!, subscriber.Reference, nextBillingAt, token);
                return new SubscribeResult { Subscription = created, Created = true };
            }
        }, ct);
    }

    public Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string reference, CancellationToken ct)
    {
        return BoundedAsync(async token =>
        {
            var customer = await FindCustomerByReferenceAsync(reference, token);
            if (customer is null)
            {
                return (IReadOnlyList<SubscriptionDto>)Array.Empty<SubscriptionDto>();
            }

            if (customer.Id is not int customerId)
            {
                throw new MaxioApiException(HttpStatusCode.BadGateway,
                    "Maxio returned a customer without an id.");
            }

            var items = await ListSubscriptionsRawAsync(customerId, token);
            var results = new List<SubscriptionDto>(items.Count);
            foreach (var item in items)
            {
                var dto = await ToSubscriptionDtoAsync(item, token);
                if (dto is not null)
                {
                    results.Add(dto);
                }
            }

            return results;
        }, ct);
    }

    private async Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken token)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: "handle:" + _settings.ProductFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 200,
                ct: token);
            return response.Select(r => r.Product).ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex) when (ex.Error.TryGetString(out _))
        {
            throw ReadFailure("list the subscription plans (the configured product family was not found)", ex);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw ReadFailure("list the subscription plans", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw HandleCancellation(token, ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Malformed(ex);
        }
    }

    private async Task<string> ReadSiteCurrencyAsync(CancellationToken token)
    {
        try
        {
            var response = await _client.Sites.ReadSite(ct: token);
            return response.Site.Currency ?? string.Empty;
        }
        catch (SdkException<RawError> ex)
        {
            throw ReadFailure("read the site currency", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw HandleCancellation(token, ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Malformed(ex);
        }
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: token);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ReadFailure("look up the customer", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw HandleCancellation(token, ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Malformed(ex);
        }
    }

    private async Task<Customer> FindOrCreateCustomerAsync(SubscriberProfile subscriber, CancellationToken token)
    {
        var existing = await FindCustomerByReferenceAsync(subscriber.Reference, token);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var request = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference
                }
            };
            var response = await _client.Customers.CreateCustomer(request, ct: token);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            var winner = await FindCustomerByReferenceAsync(subscriber.Reference, token);
            if (winner is not null)
            {
                return winner;
            }

            _logger.LogWarning(ex, "Maxio rejected a customer create for reference {Reference}.", subscriber.Reference);
            throw new MaxioApiException(HttpStatusCode.UnprocessableEntity,
                "Maxio rejected the customer profile.", ex);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            throw WriteFailure("creating the customer", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw HandleCancellation(token, ex);
        }
        catch (HttpRequestException ex)
        {
            var created = await TryFindCustomerSilentlyAsync(subscriber.Reference, token);
            if (created is not null)
            {
                return created;
            }

            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Malformed(ex);
        }
    }

    private async Task<SubscriptionDto> CreateSubscriptionAsync(string planHandle, string reference, DateTimeOffset nextBillingAt, CancellationToken token)
    {
        try
        {
            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerReference = reference,
                    NextBillingAt = nextBillingAt
                }
            };
            var response = await _client.Subscriptions.CreateSubscription(request, ct: token);
            var subscription = response.Subscription;
            if (subscription is null)
            {
                throw new MaxioApiException(HttpStatusCode.BadGateway,
                    "Maxio did not return the created subscription. Check the subscription list to confirm the result.");
            }

            return ToSubscriptionDto(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex) when (ex.Error.TryGetErrorListResponse1(out var validation))
        {
            throw new MaxioApiException(HttpStatusCode.UnprocessableEntity,
                FormatValidationMessages(validation.Errors), ex);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw WriteFailure("creating the subscription", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw HandleCancellation(token, ex);
        }
        catch (HttpRequestException ex)
        {
            var reconciled = await TryFindSubscriptionSilentlyAsync(reference, planHandle, token);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Malformed(ex);
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListSubscriptionsRawAsync(int customerId, CancellationToken token)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId, ct: token);
        }
        catch (SdkException<RawError> ex)
        {
            throw ReadFailure("list the subscriptions", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw HandleCancellation(token, ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Malformed(ex);
        }
    }

    private async Task<SubscriptionDto?> FindExistingSubscriptionAsync(int customerId, string planHandle, CancellationToken token)
    {
        var items = await ListSubscriptionsRawAsync(customerId, token);
        foreach (var item in items)
        {
            var dto = await ToSubscriptionDtoAsync(item, token);
            if (dto is null)
            {
                continue;
            }

            if (string.Equals(dto.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase) && !IsClosed(dto.State))
            {
                return dto;
            }
        }

        return null;
    }

    private async Task<SubscriptionDto?> ToSubscriptionDtoAsync(SubscriptionResponse item, CancellationToken token)
    {
        var subscription = item.Subscription;
        if (subscription is null)
        {
            return null;
        }

        if (subscription.Product is null && subscription.Id is int subscriptionId)
        {
            try
            {
                var response = await _client.Subscriptions.ReadSubscription(subscriptionId: subscriptionId, include: null, ct: token);
                subscription = response.Subscription ?? subscription;
            }
            catch (Exception ex) when (ex is SdkException<RawError> or HttpRequestException or JsonException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Could not enrich subscription {SubscriptionId} with its plan.", subscriptionId);
            }
        }

        return ToSubscriptionDto(subscription);
    }

    private async Task<SubscriptionDto?> TryFindSubscriptionSilentlyAsync(string reference, string planHandle, CancellationToken token)
    {
        try
        {
            var customer = await FindCustomerByReferenceAsync(reference, token);
            if (customer is null)
            {
                return null;
            }

            if (customer.Id is not int customerId)
            {
                return null;
            }

            return await FindExistingSubscriptionAsync(customerId, planHandle, token);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning(ex, "Reconcile after a failed subscribe could not reach Maxio.");
            return null;
        }
    }

    private async Task<Customer?> TryFindCustomerSilentlyAsync(string reference, CancellationToken token)
    {
        try
        {
            return await FindCustomerByReferenceAsync(reference, token);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning(ex, "Reconcile after a failed customer create could not reach Maxio.");
            return null;
        }
    }

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callBudget);
        try
        {
            return await action(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new MaxioApiException(HttpStatusCode.GatewayTimeout,
                "Maxio did not respond in time.");
        }
    }

    private SubscriptionDto ToSubscriptionDto(Subscription subscription)
    {
        decimal? price = subscription.ProductPriceInCents.HasValue
            ? ToMoney(subscription.ProductPriceInCents.Value)
            : subscription.Product?.PriceInCents is long productPriceInCents
                ? ToMoney(productPriceInCents)
                : null;

        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State?.Value,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            Price = price,
            Currency = subscription.Currency,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
        };
    }

    private static bool IsClosed(string? state)
    {
        return state is not null && ClosedStates.Contains(state);
    }

    private static decimal ToMoney(long cents)
    {
        return Math.Round(cents / 100m, 2);
    }

    private static DateTimeOffset ComputeNextBillingDate(Product product)
    {
        var interval = Math.Max(1, product.Interval ?? 1);
        var now = DateTimeOffset.UtcNow;
        return string.Equals(product.IntervalUnit?.Value, "day", StringComparison.OrdinalIgnoreCase)
            ? now.AddDays(interval)
            : now.AddMonths(interval);
    }

    private static string FormatValidationMessages(IReadOnlyList<string>? messages)
    {
        const int maxLength = 500;
        if (messages is null || messages.Count == 0)
        {
            return "Maxio rejected the subscription request.";
        }

        var joined = string.Join(" ", messages);
        if (joined.Length > maxLength)
        {
            joined = joined.Substring(0, maxLength) + "...";
        }

        return "Maxio rejected the subscription: " + joined;
    }

    private MaxioApiException ReadFailure(string what, Exception ex)
    {
        _logger.LogWarning(ex, "Maxio failed to {What}.", what);
        return new MaxioApiException(HttpStatusCode.BadGateway, $"Maxio could not {what}.", ex);
    }

    private MaxioApiException WriteFailure(string what, Exception ex)
    {
        _logger.LogWarning(ex, "Maxio failed while {What}.", what);
        return new MaxioApiException(HttpStatusCode.BadGateway, $"Maxio failed while {what}.", ex);
    }

    private MaxioApiException Unreachable(Exception inner)
    {
        return new MaxioApiException(HttpStatusCode.ServiceUnavailable,
            "Maxio could not be reached. Please try again later.", inner);
    }

    private MaxioApiException Malformed(Exception inner)
    {
        return new MaxioApiException(HttpStatusCode.BadGateway,
            "Maxio returned a response that could not be processed.", inner);
    }

    private MaxioApiException HandleCancellation(CancellationToken token, Exception inner)
    {
        if (token.IsCancellationRequested)
        {
            throw inner;
        }

        return new MaxioApiException(HttpStatusCode.GatewayTimeout,
            "Maxio did not respond in time.", inner);
    }
}
