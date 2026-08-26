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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Whole-call budget, applied in one place (Bounded) so every operation is capped by construction.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioBillingException(HttpStatusCode.InternalServerError,
                "Maxio:ProductFamilyHandle is not configured.");
        }

        try
        {
            var plans = new List<SubscriptionPlanDto>();
            const int perPage = 200;
            var page = 1;
            while (true)
            {
                var products = await Bounded(t => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + _settings.ProductFamilyHandle,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: perPage,
                    ct: t), ct);

                foreach (var item in products)
                {
                    var product = item.Product;
                    if (product.ArchivedAt is not null)
                    {
                        continue;
                    }

                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id,
                        Handle = product.Handle,
                        Name = product.Name,
                        Price = ToDollars(product.PriceInCents),
                        Interval = product.Interval,
                        IntervalUnit = product.IntervalUnit?.Value
                    });
                }

                if (products.Count < perPage)
                {
                    break;
                }

                page++;
            }

            return plans;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw TranslateListProductsError(ex);
        }
        catch (Exception ex) when (ex is not MaxioBillingException)
        {
            throw TranslateUnexpected(ex, ct);
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(SubscriptionUserContext user, string productHandle, CancellationToken ct)
    {
        try
        {
            var customer = await FindOrCreateCustomerAsync(user, ct);
            if (customer.Id is not int customerId)
            {
                throw new MaxioBillingException(HttpStatusCode.BadGateway,
                    "Maxio did not return a customer id.");
            }

            // Double-click guard: never create a second live subscription for the same plan.
            var existing = await FindLiveSubscriptionAsync(customerId, productHandle, ct);
            if (existing is not null)
            {
                return Map(existing);
            }

            Subscription subscription;
            try
            {
                subscription = await AttemptCreateAsync(paymentCollectionMethod: null);
            }
            catch (SdkException<CreateSubscriptionError> ex) when (IsUnprocessableEntity(ex.Error))
            {
                // The plan may be configured to collect automatically with no card on file
                // (provider message: "No payment method was on file"). Retry once with
                // invoice-style collection, which needs no payment method at signup.
                _logger.LogInformation("Subscribe for {ProductHandle} rejected with 422; retrying with remittance collection.", productHandle);
                try
                {
                    subscription = await AttemptCreateAsync(CollectionMethod.Remittance);
                }
                catch (SdkException<CreateSubscriptionError> retryEx)
                {
                    throw TranslateCreateSubscriptionError(retryEx);
                }
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                throw TranslateCreateSubscriptionError(ex);
            }

            return Map(subscription);

            async Task<Subscription> AttemptCreateAsync(CollectionMethod? paymentCollectionMethod)
            {
                try
                {
                    return await CreateSubscriptionAsync(customerId, user.UserId, productHandle, paymentCollectionMethod, ct);
                }
                catch (HttpRequestException ex)
                {
                    // Transport failures are retried by the SDK even on POST, so the write may have
                    // reached Maxio. Reconcile against provider state before reporting failure.
                    _logger.LogWarning(ex, "Transport failure creating subscription; reconciling against Maxio state.");
                    var reconciled = await FindLiveSubscriptionAsync(customerId, productHandle, ct);
                    if (reconciled is not null)
                    {
                        return reconciled;
                    }

                    throw new MaxioBillingException(HttpStatusCode.BadGateway,
                        "The billing provider could not be reached and the subscription outcome is unknown.", ex);
                }
            }
        }
        catch (Exception ex) when (ex is not MaxioBillingException)
        {
            throw TranslateUnexpected(ex, ct);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userId, CancellationToken ct)
    {
        try
        {
            // A read must not create anything: no Maxio customer yet simply means no subscriptions.
            var customer = await TryReadCustomerByReferenceAsync(userId, ct);
            if (customer is null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            if (customer.Id is not int customerId)
            {
                throw new MaxioBillingException(HttpStatusCode.BadGateway,
                    "Maxio did not return a customer id.");
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
            return subscriptions.Select(Map).ToList();
        }
        catch (Exception ex) when (ex is not MaxioBillingException)
        {
            throw TranslateUnexpected(ex, ct);
        }
    }

    private async Task<Customer> FindOrCreateCustomerAsync(SubscriptionUserContext user, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(user.UserId, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await Bounded(t => _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = user.FirstName,
                        LastName = user.LastName,
                        Email = user.Email,
                        Reference = user.UserId
                    }
                }, ct: t), ct);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Any 422 may be a duplicate-reference race (the typed error model drops the per-field
            // detail), so reconcile by re-reading the customer by reference and using the winner.
            var isUnprocessable = ex.Error.TryGetCustomerErrorResponse1(out _);
            RawError? raw = null;
            if (!isUnprocessable && ex.Error.TryGetRawError(out var fallback))
            {
                raw = fallback;
                isUnprocessable = fallback.StatusCode == HttpStatusCode.UnprocessableEntity;
            }

            if (isUnprocessable)
            {
                var winner = await TryReadCustomerByReferenceAsync(user.UserId, ct);
                if (winner is not null)
                {
                    return winner;
                }

                throw new MaxioBillingException(HttpStatusCode.UnprocessableEntity,
                    "Maxio rejected the customer profile.");
            }

            if (raw is not null)
            {
                throw FromRawError(raw);
            }

            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "Maxio rejected the customer create request.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Outcome unknown (the create may have succeeded) — reconcile before reporting failure.
            var winner = await TryReadCustomerByReferenceAsync(user.UserId, ct);
            if (winner is not null)
            {
                return winner;
            }

            throw TranslateUnexpected(ex, ct);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(t => _client.Customers.ReadCustomerByReference(reference, ct: t), ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var responses = await Bounded(t => _client.Customers.ListCustomerSubscriptions(customerId, ct: t), ct);
            return responses.Where(r => r.Subscription is not null).Select(r => r.Subscription!).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error);
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        return subscriptions.FirstOrDefault(s => s.Product?.Handle == productHandle && IsLive(s.State));
    }

    private async Task<Subscription> CreateSubscriptionAsync(
        int customerId, string userId, string productHandle, CollectionMethod? paymentCollectionMethod, CancellationToken ct)
    {
        var created = await Bounded(t => _client.Subscriptions.CreateSubscription(
            new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    Reference = $"{userId}:{productHandle}",
                    PaymentCollectionMethod = paymentCollectionMethod
                }
            }, ct: t), ct);

        return created.Subscription
            ?? throw new MaxioBillingException(HttpStatusCode.BadGateway, "Maxio returned an empty subscription response.");
    }

    private static bool IsUnprocessableEntity(CreateSubscriptionError error)
    {
        if (error.TryGetErrorListResponse1(out _))
        {
            return true;
        }

        return error.TryGetRawError(out var raw) && raw.StatusCode == HttpStatusCode.UnprocessableEntity;
    }

    private MaxioBillingException TranslateCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errorList))
        {
            return new MaxioBillingException(HttpStatusCode.UnprocessableEntity,
                "Maxio rejected the subscription: " + string.Join("; ", errorList.Errors));
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw);
        }

        return new MaxioBillingException(HttpStatusCode.BadGateway,
            "Maxio rejected the subscription request.");
    }

    private static bool IsLive(SubscriptionState? state) =>
        state == SubscriptionState.Active
        || state == SubscriptionState.Trialing
        || state == SubscriptionState.Assessing
        || state == SubscriptionState.Pending;

    private static SubscriptionDto Map(Subscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State?.Value,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        Price = ToDollars(subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents),
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit?.Value,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        // The Subscription model has no next_billing_at; current_period_ends_at is the next billing date.
        NextBillingDate = subscription.CurrentPeriodEndsAt
    };

    private static decimal? ToDollars(long? priceInCents) =>
        priceInCents is null ? null : priceInCents.Value / 100m;

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private MaxioBillingException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            return new MaxioBillingException(HttpStatusCode.NotFound,
                $"Maxio product family '{_settings.ProductFamilyHandle}' was not found.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw);
        }

        return new MaxioBillingException(HttpStatusCode.BadGateway,
            "Maxio rejected the request to list subscription plans.");
    }

    private MaxioBillingException FromRawError(RawError raw)
    {
        string? body = null;
        try
        {
            body = raw.ReadAsString();
        }
        catch (Exception readEx)
        {
            _logger.LogDebug(readEx, "Could not read Maxio error body.");
        }

        _logger.LogWarning("Maxio call failed with HTTP {StatusCode}: {Body}", (int)raw.StatusCode, body);

        return raw.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new MaxioBillingException(HttpStatusCode.BadGateway,
                    "The billing provider rejected the configured credentials."),
            HttpStatusCode.NotFound =>
                new MaxioBillingException(HttpStatusCode.NotFound,
                    "The requested billing resource was not found."),
            HttpStatusCode.UnprocessableEntity =>
                new MaxioBillingException(HttpStatusCode.UnprocessableEntity,
                    "The billing provider rejected the request."),
            >= HttpStatusCode.InternalServerError =>
                new MaxioBillingException(HttpStatusCode.BadGateway,
                    "The billing provider is currently unavailable."),
            _ => new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing provider rejected the request.")
        };
    }

    private MaxioBillingException TranslateUnexpected(Exception ex, CancellationToken ct)
    {
        switch (ex)
        {
            case JsonException:
                // A 2xx with a drifted body, or an error body that matched no generated shape.
                _logger.LogError(ex, "Maxio returned a response that could not be processed.");
                return new MaxioBillingException(HttpStatusCode.BadGateway,
                    "The billing provider returned a response that could not be processed.", ex);
            case TaskCanceledException when !ct.IsCancellationRequested:
                _logger.LogError(ex, "Maxio call exceeded the {Budget}s budget.", CallBudget.TotalSeconds);
                return new MaxioBillingException(HttpStatusCode.GatewayTimeout,
                    "The billing provider did not respond in time.", ex);
            case HttpRequestException:
            case TaskCanceledException:
                _logger.LogError(ex, "Maxio call failed at the transport level.");
                return new MaxioBillingException(HttpStatusCode.BadGateway,
                    "The billing provider could not be reached.", ex);
            default:
                _logger.LogError(ex, "Unexpected Maxio integration failure.");
                return new MaxioBillingException(HttpStatusCode.InternalServerError,
                    "An unexpected billing error occurred.", ex);
        }
    }
}
