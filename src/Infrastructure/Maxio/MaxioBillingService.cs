using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing adapter. Every SDK call goes through this boundary, which:
/// - bounds the whole call with a cancellation budget,
/// - translates SDK failures (typed Case-A errors, RawError, transport faults, unreadable
///   bodies) into <see cref="MaxioBillingException"/>,
/// - keeps customer and subscription creation idempotent via deterministic references,
/// - serializes concurrent subscribes for the same user + plan.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private const string CustomerReferencePrefix = "eshop-user-";
    private const string SubscriptionReferencePrefix = "eshop-sub-";
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeGates = new();

    public MaxioBillingService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<MaxioPlanInfo>> ListPlansAsync(CancellationToken ct = default)
    {
        return CallAsync("list-plans", isWrite: false, async token =>
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: token);

            var family = families
                .Select(f => f.ProductFamily)
                .FirstOrDefault(pf => pf is not null
                    && pf.Handle == _options.ProductFamilyHandle
                    && pf.ArchivedAt is null);

            if (family?.Id is null)
            {
                throw new MaxioBillingException(MaxioBillingErrorKind.Unexpected,
                    $"The configured product family '{_options.ProductFamilyHandle}' was not found in the billing catalog.");
            }

            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: family.Id.Value.ToString(CultureInfo.InvariantCulture),
                    dateField: null, filter: null, startDate: null, endDate: null,
                    startDatetime: null, endDatetime: null, includeArchived: false, include: null,
                    page: 1, perPage: 100, ct: token);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFoundBody))
                {
                    throw new MaxioBillingException(MaxioBillingErrorKind.NotFound,
                        "The billing catalog rejected the plan listing request.", ex);
                }
                throw MapFallbackError(ex.Error, "listing plans", ex);
            }

            var plans = products
                .Where(p => p.Product?.Handle is not null)
                .Select(p => MapPlan(p.Product!))
                .OrderBy(p => p.PriceInCents)
                .ToList();

            return (IReadOnlyList<MaxioPlanInfo>)plans;
        }, ct);
    }

    public async Task<MaxioSubscriptionResult> SubscribeAsync(MaxioSubscriber subscriber, string productHandle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.InvalidRequest, "A plan handle is required to subscribe.");
        }

        var subscriptionReference = BuildSubscriptionReference(subscriber.UserId, productHandle);
        var gate = _subscribeGates.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await CallAsync("subscribe", isWrite: true, async token =>
            {
                // Idempotency probe: a repeated subscribe (double click, retry) replays the
                // existing subscription instead of creating a second one.
                var existing = await ProbeSubscriptionAsync(subscriptionReference, token);
                if (existing is not null)
                {
                    _logger.LogInformation("Subscribe replay: user {UserId} already holds subscription {SubscriptionId} for plan {Plan}",
                        subscriber.UserId, existing.SubscriptionId, productHandle);
                    return new MaxioSubscriptionResult(existing, CreatedNew: false);
                }

                var plan = await ReadPlanAsync(productHandle, token);

                var customer = await ResolveCustomerAsync(subscriber, token);
                if (customer.Id is null)
                {
                    throw new MaxioBillingException(MaxioBillingErrorKind.Unexpected,
                        "The billing provider did not return an id for the customer.");
                }

                var subscription = await CreateSubscriptionAsync(customer.Id.Value, productHandle, subscriptionReference, token);
                _logger.LogInformation("User {UserId} subscribed to plan {Plan}: Maxio subscription {SubscriptionId} (state {State})",
                    subscriber.UserId, productHandle, subscription.SubscriptionId, subscription.State);

                return new MaxioSubscriptionResult(subscription, CreatedNew: true);
            }, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<IReadOnlyList<MaxioSubscriptionInfo>> GetSubscriptionsForUserAsync(MaxioSubscriber subscriber, CancellationToken ct = default)
    {
        return CallAsync("my-subscriptions", isWrite: false, async token =>
        {
            var customer = await LookupCustomerAsync(BuildCustomerReference(subscriber.UserId), token);
            if (customer?.Id is null)
            {
                return (IReadOnlyList<MaxioSubscriptionInfo>)Array.Empty<MaxioSubscriptionInfo>();
            }

            IReadOnlyList<SubscriptionResponse> subscriptions;
            try
            {
                subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: token);
            }
            catch (SdkException<RawError> ex)
            {
                throw MapRawError(ex.Error, "listing the user's subscriptions", ex);
            }

            var result = subscriptions
                .Where(r => r.Subscription is not null)
                .Select(r => MapSubscription(r.Subscription!))
                .ToList();

            return (IReadOnlyList<MaxioSubscriptionInfo>)result;
        }, ct);
    }

    private async Task<Customer?> LookupCustomerAsync(string reference, CancellationToken token)
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
            throw MapRawError(ex.Error, $"looking up customer '{reference}'", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Unexpected,
                "The billing provider returned a response that could not be processed.", ex);
        }
    }

    /// <summary>
    /// Resolves the Maxio customer for the user, creating it on first sight. Customer
    /// reference uniqueness is enforced by the provider, so a concurrent create cannot
    /// produce two customers: on a duplicate-reference rejection we simply re-look-up.
    /// </summary>
    private async Task<Customer> ResolveCustomerAsync(MaxioSubscriber subscriber, CancellationToken token)
    {
        var reference = BuildCustomerReference(subscriber.UserId);
        var existing = await LookupCustomerAsync(reference, token);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveNames(subscriber.Email ?? subscriber.UserName);
        var createBody = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email ?? subscriber.UserName,
                Reference = reference,
            },
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(createBody, ct: token);
            return response.Customer!;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // 422 also fires when the reference already exists (provider-enforced uniqueness):
            // recover by re-looking-up instead of failing the subscribe.
            var recovered = await LookupCustomerAsync(reference, token);
            if (recovered is not null)
            {
                return recovered;
            }

            string detail = ex.Error.TryGetRawError(out var raw)
                ? raw.ReadAsString()
                : ex.Message;
            _logger.LogError(ex, "Customer creation rejected for reference {Reference}: {Detail}", reference, detail);
            throw new MaxioBillingException(MaxioBillingErrorKind.InvalidRequest,
                "The billing provider rejected customer creation.", ex);
        }
        catch (JsonException ex)
        {
            // A malformed non-2xx body surfaces as JsonException and destroys the status: the
            // request was rejected but the reason was lost. Recover by re-looking-up.
            var recovered = await LookupCustomerAsync(reference, token);
            if (recovered is not null)
            {
                return recovered;
            }

            throw new MaxioBillingException(MaxioBillingErrorKind.Unexpected,
                "The billing provider returned a response that could not be processed.", ex);
        }
    }

    private async Task<MaxioSubscriptionInfo?> ProbeSubscriptionAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, ct: token);
            return response.Subscription is null ? null : MapSubscription(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null; // 404: no subscription with that reference yet
            }
            if (ex.Error.TryGetRawError(out var raw) && raw.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            _logger.LogError(ex, "Subscription probe for reference {Reference} failed", reference);
            throw new MaxioBillingException(MaxioBillingErrorKind.ProviderUnavailable,
                "The billing provider could not confirm subscription state.", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Unexpected,
                "The billing provider returned a response that could not be processed.", ex);
        }
    }

    /// <summary>
    /// Validates the plan is subscribable (exists, in the configured family, needs no payment
    /// method) and returns its catalog entry before any enrollment is attempted.
    /// </summary>
    private async Task<Product> ReadPlanAsync(string productHandle, CancellationToken token)
    {
        Product product;
        try
        {
            var response = await _client.Products.ReadProductByHandle(productHandle, ct: token);
            product = response.Product!;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.NotFound, $"Unknown subscription plan '{productHandle}'.", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, $"reading plan '{productHandle}'", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Unexpected,
                "The billing provider returned a response that could not be processed.", ex);
        }

        if (product.ProductFamily?.Handle is string familyHandle && familyHandle != _options.ProductFamilyHandle)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.InvalidRequest,
                $"Plan '{productHandle}' does not belong to the subscribable catalog.");
        }

        // require_credit_card makes payment info mandatory at signup — reject up-front rather
        // than discovering it as a provider 422. (request_credit_card merely asks for a card
        // and does not block a payment-free enrollment.)
        if (product.RequireCreditCard == true)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.InvalidRequest,
                $"Plan '{productHandle}' requires a payment method and cannot be subscribed without one.");
        }

        return product;
    }

    private async Task<MaxioSubscriptionInfo> CreateSubscriptionAsync(int customerId, string productHandle, string subscriptionReference, CancellationToken token)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = subscriptionReference,
                // No payment profile is captured in this flow: remittance collection bills the
                // customer outside a stored card, so enrollment works without card capture/3-DS.
                PaymentCollectionMethod = CollectionMethod.Remittance,
            },
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct: token);
            return response.Subscription is null
                ? throw new MaxioBillingException(MaxioBillingErrorKind.Unexpected,
                    "The billing provider did not return the created subscription.")
                : MapSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // Duplicate-reference 422 (provider may enforce subscription reference uniqueness):
            // treat as an idempotent replay if the subscription now exists.
            var replay = await ProbeSubscriptionAsync(subscriptionReference, token);
            if (replay is not null)
            {
                return replay;
            }

            string detail = ex.Error.TryGetErrorListResponse1(out var errors)
                ? string.Join("; ", errors.Errors)
                : ex.Error.TryGetRawError(out var raw) ? raw.ReadAsString() : ex.Message;
            _logger.LogError(ex, "Subscription creation rejected for reference {Reference}: {Detail}", subscriptionReference, detail);
            throw new MaxioBillingException(MaxioBillingErrorKind.InvalidRequest,
                $"The billing provider rejected the subscription: {detail}", ex);
        }
        catch (JsonException ex)
        {
            var replay = await ProbeSubscriptionAsync(subscriptionReference, token);
            if (replay is not null)
            {
                return replay;
            }

            throw new MaxioBillingException(MaxioBillingErrorKind.InvalidRequest,
                "The billing provider rejected the subscription; the reason could not be read.", ex);
        }
    }

    /// <summary>
    /// The single home of the whole-call budget: every SDK call runs under a linked
    /// cancellation token capped at <see cref="TotalCallBudget"/>, so the worst case a
    /// caller experiences is bounded regardless of SDK retry behaviour.
    /// </summary>
    private async Task<T> CallAsync<T>(string operation, bool isWrite, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TotalCallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller went away — not a billing failure
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Maxio call {Operation} exceeded the {Budget} budget", operation, TotalCallBudget);
            throw new MaxioBillingException(MaxioBillingErrorKind.ProviderUnavailable,
                "The billing provider did not respond in time.", ex);
        }
        catch (JsonException ex) when (isWrite)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.InvalidRequest,
                $"The billing provider rejected {operation}; the reason could not be read.", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Unexpected,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Maxio call {Operation} failed: provider unreachable", operation);
            throw new MaxioBillingException(MaxioBillingErrorKind.ProviderUnavailable,
                "The billing provider is unreachable.", ex);
        }
    }

    private MaxioBillingException MapRawError(RawError raw, string what, Exception inner)
    {
        _logger.LogError(inner, "Maxio call while {What} failed with HTTP {Status}: {Body}",
            what, (int)raw.StatusCode, SafeRead(raw));

        return raw.StatusCode >= HttpStatusCode.InternalServerError || raw.StatusCode == 0
            ? new MaxioBillingException(MaxioBillingErrorKind.ProviderUnavailable,
                "The billing provider failed to process the request.", inner)
            : new MaxioBillingException(MaxioBillingErrorKind.InvalidRequest,
                "The billing provider rejected the request.", inner);
    }

    private MaxioBillingException MapFallbackError(ApiError error, string what, Exception inner)
    {
        var body = error.TryGetRawError(out var raw) ? SafeRead(raw) : null;
        _logger.LogError(inner, "Maxio call while {What} failed: {Body}", what, body);
        return new MaxioBillingException(MaxioBillingErrorKind.ProviderUnavailable,
            "The billing provider failed to process the request.", inner);
    }

    private static string SafeRead(RawError raw)
    {
        try
        {
            return raw.ReadAsString() ?? string.Empty;
        }
        catch
        {
            return "<unreadable body>";
        }
    }

    private static MaxioPlanInfo MapPlan(Product product) =>
        new(product.Handle!,
            product.Name ?? product.Handle!,
            product.PriceInCents ?? 0,
            product.Interval ?? 1,
            product.IntervalUnit?.Value ?? IntervalUnit.Month.Value);

    private static MaxioSubscriptionInfo MapSubscription(Subscription subscription) =>
        new(subscription.Id ?? 0,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            subscription.Product?.Interval ?? 1,
            subscription.Product?.IntervalUnit?.Value ?? IntervalUnit.Month.Value,
            subscription.State?.Value ?? "unknown",
            subscription.CurrentPeriodEndsAt);

    private static string BuildCustomerReference(string userId) => $"{CustomerReferencePrefix}{userId}";

    private static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"{SubscriptionReferencePrefix}{userId}:{productHandle}";

    private static (string FirstName, string LastName) DeriveNames(string emailOrUserName)
    {
        var local = emailOrUserName.Contains('@')
            ? emailOrUserName.Split('@')[0]
            : emailOrUserName;
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return (parts.Length > 0 ? Capitalize(parts[0]) : "eShopOnWeb",
                parts.Length > 1 ? Capitalize(parts[^1]) : "Customer");
    }

    private static string Capitalize(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];
}
