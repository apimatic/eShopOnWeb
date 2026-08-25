using System;
using System.Collections.Concurrent;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Closes the check-then-create window app-side: one in-flight subscribe per user.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, MaxioSettings settings, IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            ProductFamily family;
            try
            {
                var families = await _client.ProductFamilies.ListProductFamilies(
                    dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct);
                family = families.Select(f => f.ProductFamily)
                    .FirstOrDefault(f => f?.Handle == _settings.ProductFamilyHandle)
                    ?? throw new MaxioBillingException(
                        $"Maxio product family '{_settings.ProductFamilyHandle}' was not found on the configured site.");
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRaw(ex, "listing product families");
            }

            try
            {
                var products = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: family.Id!.Value.ToString(),
                    dateField: null, filter: null, startDate: null, endDate: null,
                    startDatetime: null, endDatetime: null, includeArchived: null, include: null,
                    page: 1, perPage: 100, ct: ct);

                return products.Select(p => p.Product)
                    .Where(p => p.ArchivedAt is null)
                    .Select(MapPlan)
                    .ToList();
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFoundMessage))
                {
                    throw new MaxioBillingException($"Maxio rejected the product-family lookup: {notFoundMessage}", HttpStatusCode.NotFound, ex);
                }
                else if (ex.Error.TryGetRawError(out var raw))
                {
                    throw TranslateRaw(raw, "listing plans", ex);
                }
                throw new MaxioBillingException("Maxio rejected the product-family lookup.", null, ex);
            }
        }, cancellationToken);
    }

    public async Task<SubscriptionDto> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException("A product handle is required.", HttpStatusCode.BadRequest);
        }

        var gate = SubscribeLocks.GetOrAdd(user.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await Bounded(async ct =>
            {
                var customer = await EnsureCustomerAsync(user, ct);

                var existing = await FindLiveSubscriptionAsync(customer, productHandle, ct);
                if (existing is not null)
                {
                    _logger.LogInformation(
                        "User {UserId} already has a live subscription {SubscriptionId} for '{ProductHandle}'; returning it instead of creating a duplicate.",
                        user.UserId, existing.Id ?? 0, productHandle);
                    return MapSubscription(existing);
                }

                var reference = $"eshop-{user.UserId}-{productHandle}";
                try
                {
                    var created = await _client.Subscriptions.CreateSubscription(
                        body: new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = productHandle,
                                CustomerId = customer.Id,
                                Reference = reference,
                                // Merchant collects off-platform; the seeded plans take no card at signup.
                                PaymentCollectionMethod = CollectionMethod.Remittance
                            }
                        },
                        ct: ct);

                    if (created.Subscription is null)
                    {
                        throw new MaxioBillingException("Maxio returned an empty subscription response.");
                    }
                    return MapSubscription(created.Subscription);
                }
                catch (SdkException<CreateSubscriptionError> ex)
                {
                    if (ex.Error.TryGetErrorListResponse1(out var errorList))
                    {
                        var messages = string.Join("; ", errorList.Errors);
                        throw new MaxioBillingException($"Maxio rejected the subscription: {messages}", HttpStatusCode.UnprocessableEntity, ex);
                    }
                    else if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw TranslateRaw(raw, "creating the subscription", ex);
                    }
                    throw new MaxioBillingException("Maxio rejected the subscription request.", null, ex);
                }
            }, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(BillingUser user, CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            var customer = await FindCustomerAsync(user.UserId, ct);
            if (customer is null)
            {
                return (IReadOnlyList<SubscriptionDto>)Array.Empty<SubscriptionDto>();
            }

            var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id!.Value, ct);
            return subscriptions.Select(MapSubscription).ToList();
        }, cancellationToken);
    }

    private async Task<Customer> EnsureCustomerAsync(BillingUser user, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(user.UserId, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = user.FirstName,
                        LastName = user.LastName,
                        Email = user.Email,
                        Reference = user.UserId
                    }
                },
                ct: ct);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // 422 — most likely a lost race on the unique reference; the customer now exists.
                var winner = await FindCustomerAsync(user.UserId, ct);
                if (winner is not null)
                {
                    return winner;
                }
                throw new MaxioBillingException("Maxio rejected the customer record.", HttpStatusCode.UnprocessableEntity, ex);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "creating the customer", ex);
            }
            throw new MaxioBillingException("Maxio rejected the customer record.", null, ex);
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "looking up the customer");
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(Customer customer, string productHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id!.Value, ct);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            IsLiveState(s.State));
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var responses = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
            return responses.Select(r => r.Subscription).Where(s => s is not null).Cast<Subscription>().ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "listing subscriptions");
        }
    }

    private static bool IsLiveState(SubscriptionState? state)
        => state == SubscriptionState.Active
        || state == SubscriptionState.Trialing
        || state == SubscriptionState.AwaitingSignup
        || state == SubscriptionState.PastDue
        || state == SubscriptionState.OnHold;

    private static SubscriptionPlanDto MapPlan(Product product) => new()
    {
        ProductId = product.Id ?? 0,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description ?? string.Empty,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit?.Value ?? IntervalUnit.Month.Value
    };

    private static SubscriptionDto MapSubscription(Subscription subscription) => new()
    {
        SubscriptionId = subscription.Id ?? 0,
        State = subscription.State?.Value ?? string.Empty,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        ProductPriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency ?? string.Empty,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingDate = subscription.CurrentPeriodEndsAt
    };

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model, or an error body that broke
            // during error-object construction. Outcome unknown — never a domain "no".
            _logger.LogWarning("Maxio returned a response that could not be processed: {Error}", ex.Message);
            throw new MaxioBillingException("The billing provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Maxio is unreachable or timed out: {Error}", ex.Message);
            throw new MaxioBillingException("The billing provider is unreachable or timed out.", HttpStatusCode.ServiceUnavailable, ex);
        }
    }

    private MaxioBillingException TranslateRaw(SdkException<RawError> ex, string operation)
        => TranslateRaw(ex.Error, operation, ex);

    private MaxioBillingException TranslateRaw(RawError raw, string operation, Exception inner)
    {
        _logger.LogWarning("Maxio {Operation} failed with HTTP {StatusCode}: {Body}",
            operation, (int)raw.StatusCode, raw.ReadAsString());
        return new MaxioBillingException(
            $"The billing provider failed while {operation} (HTTP {(int)raw.StatusCode}).",
            raw.StatusCode, inner);
    }
}
