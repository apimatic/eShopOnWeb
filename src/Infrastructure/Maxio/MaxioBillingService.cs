using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio-backed implementation of <see cref="IMaxioBillingService"/>. Every provider call is wrapped by a
/// single error boundary (<see cref="ExecuteAsync{T}"/>) that translates SDK, transport and deserialization
/// failures into <see cref="MaxioBillingException"/> with a caller-safe message and an appropriate HTTP
/// status, and bounds the whole call with a cancellation budget. Subscribe is made idempotent by a
/// deterministic subscription reference, a find-before-create, and a per-(user, plan) in-process lock.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // Whole-call budget (the SDK's own Timeout/retries only bound a single attempt).
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Maxio's Product model carries no currency; this sandbox bills in the site default (USD).
    private const string DisplayCurrency = "USD";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    // Serializes concurrent subscribe attempts for the same (user, plan) so a double-click cannot race
    // past the find-before-create check. Single-instance scope; a multi-instance deployment would move
    // this to a shared lock/uniqueness constraint keyed on the subscription reference.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new(StringComparer.Ordinal);

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync<IReadOnlyList<SubscriptionPlan>>("list plans", async token =>
        {
            var familyId = await ResolveProductFamilyIdAsync(token);

            var plans = new List<SubscriptionPlan>();
            var page = 1;
            const int perPage = 100;
            while (true)
            {
                var batch = await ListProductsAsync(familyId, page, perPage, token);
                foreach (var product in batch)
                {
                    if (product.Product != null)
                    {
                        plans.Add(MapPlan(product.Product));
                    }
                }

                if (batch.Count < perPage)
                {
                    break;
                }

                page++;
            }

            return plans;
        }, cancellationToken);
    }

    public Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string productHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException("A plan handle is required to subscribe.", 400);
        }

        var handle = productHandle.Trim();
        var subscriptionReference = $"{subscriber.Reference}:{handle}";

        return ExecuteAsync("subscribe", async token =>
        {
            var gate = _subscribeLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token);
            try
            {
                // Idempotency: if this (user, plan) already has a subscription, return it rather than duplicating.
                var existing = await FindSubscriptionAsync(subscriptionReference, token);
                if (existing != null)
                {
                    return new SubscribeResult(MapSubscription(existing), alreadyExisted: true);
                }

                var customerId = await EnsureCustomerAsync(subscriber, token);
                var created = await CreateSubscriptionAsync(handle, customerId, subscriptionReference, token);
                return new SubscribeResult(MapSubscription(created), alreadyExisted: false);
            }
            finally
            {
                gate.Release();
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        return ExecuteAsync<IReadOnlyList<CustomerSubscription>>("list subscriptions", async token =>
        {
            var customer = await ReadCustomerAsync(subscriber.Reference, token);
            if (customer?.Id is not int customerId)
            {
                // No Maxio customer yet → the shopper simply has no subscriptions.
                return Array.Empty<CustomerSubscription>();
            }

            IReadOnlyList<SubscriptionResponse> subscriptions;
            try
            {
                subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, token);
            }
            catch (SdkException<RawError> ex)
            {
                throw Translate(ex.Error, "list your subscriptions");
            }

            var result = new List<CustomerSubscription>();
            foreach (var subscription in subscriptions)
            {
                if (subscription.Subscription != null)
                {
                    result.Add(MapSubscription(subscription.Subscription));
                }
            }

            return result;
        }, cancellationToken);
    }

    // ---- provider-call helpers (each owns its typed catch, throwing MaxioBillingException on real errors) ----

    private async Task<string> ResolveProductFamilyIdAsync(CancellationToken token)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(null, null, null, null, null, token);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, "load the subscription catalog");
        }

        foreach (var family in families)
        {
            if (string.Equals(family.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal)
                && family.ProductFamily?.Id is int id)
            {
                return id.ToString(CultureInfo.InvariantCulture);
            }
        }

        _logger.LogError("Configured Maxio product family handle '{Handle}' was not found on the site.", _settings.ProductFamilyHandle);
        throw new MaxioBillingException("The subscription catalog is not available.", 502);
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(string familyId, int page, int perPage, CancellationToken token)
    {
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: page,
                perPage: perPage,
                ct: token);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                // 404 — the family id no longer resolves.
                throw new MaxioBillingException("The subscription catalog is not available.", 502);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, "load the subscription catalog");
            }

            throw Translate(ex, "load the subscription catalog");
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, token);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null; // not found → caller decides whether to create
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, "look up your billing profile");
        }
    }

    private async Task<int> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken token)
    {
        var existing = await ReadCustomerAsync(subscriber.Reference, token);
        if (existing?.Id is int existingId)
        {
            return existingId;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference
                }
            }, token);

            if (response.Customer?.Id is int createdId)
            {
                return createdId;
            }

            throw new MaxioBillingException("Your billing profile could not be created. Please try again.", 502);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent create loses the reference-uniqueness race and 422s — re-read and use the winner.
            var raced = await ReadCustomerAsync(subscriber.Reference, token);
            if (raced?.Id is int racedId)
            {
                return racedId;
            }

            // The typed 422 payload models only per_page/price_point, so the real validation message
            // is not available here; log the exception and surface a safe rejection.
            _logger.LogError(ex, "Maxio CreateCustomer was rejected for reference {Reference}.", subscriber.Reference);

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, "create your billing profile");
            }

            throw new MaxioBillingException("Your billing profile could not be created because the details were rejected.", 422, ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, token);
            return response.Subscription; // may be null on a 2xx — treat as not-found
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null; // 404 — no such subscription
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if ((int)raw.StatusCode == 404)
                {
                    return null;
                }

                throw Translate(raw, "look up your subscription");
            }

            throw Translate(ex, "look up your subscription");
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(string productHandle, int customerId, string subscriptionReference, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    Reference = subscriptionReference,
                    // Remittance (invoice) collection enrolls the customer without a stored payment
                    // method — the plans are configured as "payment method not required", so we must not
                    // trigger an immediate automatic card charge that would fail with no card on file.
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            }, token);

            if (response.Subscription is null)
            {
                throw new MaxioBillingException("Your subscription could not be created. Please try again.", 502);
            }

            return response.Subscription;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                var detail = errors.Errors is { Count: > 0 }
                    ? string.Join("; ", errors.Errors)
                    : "the request was rejected";
                _logger.LogWarning("Maxio CreateSubscription 422 for {Reference}: {Detail}", subscriptionReference, detail);
                throw new MaxioBillingException($"Your subscription could not be created: {detail}", 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, "create your subscription");
            }

            throw Translate(ex, "create your subscription");
        }
    }

    // ---- projections ----

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Currency = DisplayCurrency,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        ProductFamilyName = product.ProductFamily?.Name
    };

    private static CustomerSubscription MapSubscription(Subscription subscription) => new()
    {
        SubscriptionId = subscription.Id ?? 0,
        State = subscription.State?.Value,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        Currency = DisplayCurrency,
        NextBillingAt = subscription.CurrentPeriodEndsAt,
        CustomerReference = subscription.Customer?.Reference
    };

    // ---- error boundary ----

    /// <summary>
    /// Bounds the whole operation with a linked cancellation budget and converts the failures that can
    /// escape the per-call typed catches — transport errors, deserialization faults, and timeouts — into
    /// <see cref="MaxioBillingException"/> with a caller-safe message and status.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(string operation, Func<CancellationToken, Task<T>> body, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        try
        {
            return await body(cts.Token);
        }
        catch (MaxioBillingException)
        {
            throw; // already translated and caller-safe
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the caller (or the disconnected client) cancelled — let the host handle it
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Maxio {Operation} exceeded the {Budget} call budget.", operation, CallBudget);
            throw new MaxioBillingException("The billing service did not respond in time. Please try again.", 504, ex);
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches its model. Outcome unknown → surface as an upstream failure
            // (never mapped onto a domain "absence" — misses come only from explicit 404 handling above).
            _logger.LogError(ex, "Maxio {Operation} returned a response that could not be parsed.", operation);
            throw new MaxioBillingException("The billing service returned a response that could not be processed.", 502, ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Maxio {Operation} failed — provider unreachable.", operation);
            throw new MaxioBillingException("The billing service is currently unreachable. Please try again.", 503, ex);
        }
    }

    private MaxioBillingException Translate(RawError raw, string action)
    {
        var status = (int)raw.StatusCode;
        string body;
        try
        {
            body = raw.ReadAsString();
        }
        catch (Exception readEx)
        {
            _logger.LogWarning(readEx, "Maxio error body during {Action} could not be read.", action);
            body = "<unreadable>";
        }

        _logger.LogError("Maxio error during {Action}: HTTP {Status} {Body}", action, status, body);

        // A provider 4xx the caller can act on maps to that same 4xx; anything else is an upstream fault.
        var clientStatus = status is >= 400 and < 500 ? status : 502;
        return new MaxioBillingException($"We could not {action} right now. Please try again.", clientStatus);
    }

    private MaxioBillingException Translate(Exception ex, string action)
    {
        _logger.LogError(ex, "Maxio error during {Action}.", action);
        return new MaxioBillingException($"We could not {action} right now. Please try again.", 502, ex);
    }
}
