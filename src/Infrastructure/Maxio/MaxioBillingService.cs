using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// <para>
/// Idempotency is layered:
/// <list type="bullet">
/// <item>The billing customer is keyed on the eShop user id (customer <c>reference</c>), looked up
/// before creating, so repeated subscribes never create a second customer. A create that loses a
/// race to a concurrent request (422 on the unique reference) falls back to a re-lookup.</item>
/// <item>Before creating a subscription we return any existing live subscription to the same plan.</item>
/// <item>The create call carries a <c>uniqueness_token</c> derived from the user + plan, which closes
/// the small concurrent window where two requests both pass the pre-check; the duplicate gets a 409
/// and is resolved to the winning subscription.</item>
/// </list>
/// </para>
/// </summary>
public sealed class MaxioBillingService : ISubscriptionBillingService
{
    // Terminal states do not block re-subscribing to the same plan.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    // Per-(user, plan) gates used to serialize concurrent subscribe attempts within this process.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    private readonly MaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioApiClient client, MaxioSettings settings, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"product_families/handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}/products.json?per_page=200";
        var result = await _client.GetAsync(path, cancellationToken);
        if (!result.IsSuccess)
        {
            throw ToException(result, "Failed to list subscription plans.");
        }

        var envelopes = result.Deserialize<List<MaxioProductEnvelope>>() ?? new List<MaxioProductEnvelope>();
        return envelopes
            .Where(e => e.Product is not null)
            .Select(e => MapPlan(e.Product!))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(BillingCustomerInfo customer, string planHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionBillingException("A subscription plan handle is required.", SubscriptionBillingError.Validation);
        }

        // Validate the plan belongs to the configured product family (never subscribe to an arbitrary product).
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionBillingException(
                $"Unknown subscription plan '{planHandle}'.",
                SubscriptionBillingError.NotFound,
                details: plans.Select(p => p.Handle).ToArray());
        }

        // This integration does not capture card details, so it can only enroll plans that do not
        // require a payment method (billed by remittance/invoice). Fail clearly for card-required plans.
        if (plan.RequiresPaymentMethod)
        {
            throw new SubscriptionBillingException(
                $"Plan '{planHandle}' requires a stored payment method, which this integration does not collect.",
                SubscriptionBillingError.Validation);
        }

        // Serialize a user's concurrent subscribe attempts to the same plan within this instance so
        // the pre-check reliably catches duplicates (closes the near-simultaneous double-click window).
        var gate = GetGate(customer.Reference, planHandle);
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Ensure a single billing customer exists for this user (idempotent by reference).
            var maxioCustomer = await EnsureCustomerAsync(customer, cancellationToken);

            // If a live subscription to this plan already exists, return it instead of creating another.
            var existing = await FindLiveSubscriptionAsync(maxioCustomer.Id, planHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Returning existing {State} subscription {SubscriptionId} for customer {CustomerId} on plan {Plan}.", existing.State, existing.Id, maxioCustomer.Id, planHandle);
                return new SubscribeResult(MapSubscription(existing), alreadyExisted: true);
            }

            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscriptionBody
                {
                    ProductHandle = planHandle,
                    CustomerId = maxioCustomer.Id,
                    Reference = $"{customer.Reference}:{planHandle}",
                    // Invoice billing: no card required, an invoice is issued at renewal.
                    PaymentCollectionMethod = "remittance"
                },
                // Random per request: guards network-timeout retries (same token reused across the
                // client's own retries) without poisoning future attempts after a failed create.
                UniquenessToken = Guid.NewGuid().ToString()
            };

            var result = await _client.PostAsync("subscriptions.json", request, cancellationToken);

            if (result.StatusCode == 409)
            {
                // A retried request found its earlier submission already processed; resolve to it.
                _logger.LogInformation("Duplicate subscribe detected for customer {CustomerId} on plan {Plan}; resolving winner.", maxioCustomer.Id, planHandle);
                var winner = await FindLiveSubscriptionWithRetryAsync(maxioCustomer.Id, planHandle, cancellationToken);
                if (winner is not null)
                {
                    return new SubscribeResult(MapSubscription(winner), alreadyExisted: true);
                }

                throw new SubscriptionBillingException(
                    "A subscription request for this plan is already being processed. Please try again in a moment.",
                    SubscriptionBillingError.Conflict);
            }

            if (!result.IsSuccess)
            {
                throw ToException(result, $"Failed to create subscription for plan '{planHandle}'.");
            }

            var subscription = result.Deserialize<MaxioSubscriptionEnvelope>()?.Subscription;
            if (subscription is null)
            {
                throw new SubscriptionBillingException("The billing system returned an empty subscription.", SubscriptionBillingError.Upstream);
            }

            _logger.LogInformation("Created subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {Plan}.", subscription.Id, subscription.State, maxioCustomer.Id, planHandle);
            return new SubscribeResult(MapSubscription(subscription), alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(BillingCustomerInfo customer, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var maxioCustomer = await LookupCustomerAsync(customer.Reference, cancellationToken);
        if (maxioCustomer is null)
        {
            // No billing customer yet means the user has never subscribed.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListSubscriptionsAsync(maxioCustomer.Id, cancellationToken);
        return subscriptions
            .Select(MapSubscription)
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    // ---- Customer helpers ----

    private async Task<MaxioCustomer> EnsureCustomerAsync(BillingCustomerInfo customer, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerAsync(customer.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomerBody
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var create = await _client.PostAsync("customers.json", createRequest, cancellationToken);
        if (create.IsSuccess)
        {
            var created = create.Deserialize<MaxioCustomerEnvelope>()?.Customer;
            if (created is not null)
            {
                _logger.LogInformation("Created billing customer {CustomerId} for reference {Reference}.", created.Id, customer.Reference);
                return created;
            }
        }

        if (create.StatusCode == 422)
        {
            // The reference must be unique; a concurrent request likely created it first. Re-lookup.
            var raced = await LookupCustomerAsync(customer.Reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
        }

        throw ToException(create, "Failed to create billing customer.");
    }

    private async Task<MaxioCustomer?> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var result = await _client.GetAsync(path, cancellationToken);

        if (result.StatusCode == 404)
        {
            return null;
        }

        if (!result.IsSuccess)
        {
            throw ToException(result, "Failed to look up billing customer.");
        }

        return result.Deserialize<MaxioCustomerEnvelope>()?.Customer;
    }

    // ---- Subscription helpers ----

    private async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json?per_page=200";
        var result = await _client.GetAsync(path, cancellationToken);
        if (!result.IsSuccess)
        {
            throw ToException(result, "Failed to list subscriptions.");
        }

        var envelopes = result.Deserialize<List<MaxioSubscriptionEnvelope>>() ?? new List<MaxioSubscriptionEnvelope>();
        return envelopes.Where(e => e.Subscription is not null).Select(e => e.Subscription!).ToList();
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Where(s => !TerminalStates.Contains(s.State))
            .Where(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionWithRetryAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var found = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Per-(user, plan) async gate that serializes concurrent subscribe attempts within this process,
    /// so the "already subscribed?" pre-check reliably de-duplicates near-simultaneous double-clicks.
    /// (For a multi-instance deployment this would be complemented by a distributed lock.)
    /// </summary>
    private static SemaphoreSlim GetGate(string reference, string planHandle)
    {
        var key = $"{reference}|{planHandle}".ToLowerInvariant();
        return Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }

    // ---- Mapping ----

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        FormattedPrice = FormatPrice(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard,
        Taxable = product.Taxable,
        ProductId = product.Id
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : product?.PriceInCents ?? 0;

        return new CustomerSubscription
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            PriceInCents = priceInCents,
            FormattedPrice = FormatPrice(priceInCents),
            Interval = product?.Interval ?? 0,
            IntervalUnit = product?.IntervalUnit ?? string.Empty,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            CustomerId = subscription.Customer?.Id ?? 0
        };
    }

    private static string FormatPrice(long priceInCents)
        => (priceInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    // ---- Config + error mapping ----

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new SubscriptionBillingException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl) and Maxio:ProductFamilyHandle.",
                SubscriptionBillingError.NotConfigured);
        }
    }

    private SubscriptionBillingException ToException(MaxioResult result, string message)
    {
        var details = result.ExtractErrors();
        var error = result.StatusCode switch
        {
            401 or 403 => SubscriptionBillingError.NotConfigured,
            404 => SubscriptionBillingError.NotFound,
            409 => SubscriptionBillingError.Conflict,
            422 => SubscriptionBillingError.Validation,
            _ => SubscriptionBillingError.Upstream
        };

        _logger.LogError("Maxio call failed with {Status}: {Message} {Details}", result.StatusCode, message, string.Join("; ", details));
        return new SubscriptionBillingException(message, error, details);
    }
}
