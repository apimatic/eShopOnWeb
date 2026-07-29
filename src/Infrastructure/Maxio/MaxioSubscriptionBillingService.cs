using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Exceptions;
using AdvancedBilling.Standard.Models;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> implemented against Maxio Advanced Billing via the official
/// <c>Maxio.AdvancedBillingSdk</c>. Maxio is the system of record: customers and subscriptions live there and
/// are keyed to eShopOnWeb users by the customer <c>reference</c>, so no local persistence is required.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Serialises subscribe operations per user (by reference) within this process so a double-click cannot
    // create two customers / two subscriptions. Cross-process repeats are additionally guarded by the
    // reference-based customer lookup and the existing-subscription check below.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly AdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        AdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireFamilyHandle();

        List<ProductResponse> products;
        try
        {
            products = await _client.ProductsController.ListProductsAsync(new ListProductsInput
            {
                PerPage = 200,
                IncludeArchived = false,
            });
        }
        catch (ApiException ex)
        {
            throw Wrap("list subscription plans", ex);
        }

        return products
            .Where(pr => pr?.Product is not null)
            .Select(pr => pr.Product)
            .Where(p => string.Equals(p.ProductFamily?.Handle, familyHandle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.PriceInCents ?? 0)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscriberInfo subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        if (string.IsNullOrWhiteSpace(subscriber.Reference))
        {
            throw new SubscriptionBillingException("Cannot subscribe: the subscriber reference is empty.");
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new PlanNotFoundException(planHandle ?? string.Empty);
        }

        // Only allow subscribing to a plan that actually belongs to the configured product family.
        var plan = (await GetPlansAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new PlanNotFoundException(planHandle);

        var gate = Locks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            // Idempotency: if the customer already has a live subscription to this plan, return it unchanged.
            var existing = await FindLiveSubscriptionAsync(customer.Id!.Value, plan.Handle);
            if (existing is not null)
            {
                _logger.LogInformation("Reusing existing subscription {SubscriptionId} for customer {CustomerId} on plan {Plan}.",
                    existing.Id, customer.Id, plan.Handle);
                return MapSubscription(existing)!;
            }

            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    // The demo plans do not require a stored payment method. Remittance (invoice) collection lets
                    // a shopper enrol without card capture / 3-DS; the subscription activates and is invoiced.
                    PaymentCollectionMethod = CollectionMethod.Remittance,
                },
            };

            SubscriptionResponse created;
            try
            {
                created = await _client.SubscriptionsController.CreateSubscriptionAsync(body);
            }
            catch (ApiException ex)
            {
                throw Wrap($"subscribe to plan '{plan.Handle}'", ex);
            }

            _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId} on plan {Plan}.",
                created.Subscription?.Id, customer.Id, plan.Handle);
            return MapSubscription(created.Subscription)!;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var customer = await FindCustomerAsync(subscriber.Reference);
        if (customer?.Id is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        List<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.CustomersController.ListCustomerSubscriptionsAsync(customer.Id.Value);
        }
        catch (ApiException ex)
        {
            throw Wrap("list your subscriptions", ex);
        }

        return subscriptions
            .Select(s => MapSubscription(s.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    // --- Customer provisioning -------------------------------------------------------------------

    private async Task<Customer> EnsureCustomerAsync(SubscriberInfo subscriber, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(subscriber.Reference);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(subscriber);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference,
            },
        };

        try
        {
            var created = await _client.CustomersController.CreateCustomerAsync(body);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.",
                created.Customer?.Id, subscriber.Reference);
            return created.Customer;
        }
        catch (CustomerErrorResponseException ex)
        {
            // A concurrent request may have created the customer between our read and this create
            // (reference must be unique). Re-read and reuse it rather than failing.
            _logger.LogWarning(ex, "Create customer for reference {Reference} was rejected; re-reading by reference.", subscriber.Reference);
            var raced = await FindCustomerAsync(subscriber.Reference);
            if (raced is not null)
            {
                return raced;
            }
            throw Wrap("create billing customer", ex);
        }
        catch (ApiException ex)
        {
            throw Wrap("create billing customer", ex);
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        try
        {
            var response = await _client.CustomersController.ReadCustomerByReferenceAsync(reference);
            return response?.Customer;
        }
        catch (ApiException ex) when (ex.ResponseCode == 404)
        {
            return null;
        }
        catch (ApiException ex)
        {
            throw Wrap("look up billing customer", ex);
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string planHandle)
    {
        List<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.CustomersController.ListCustomerSubscriptionsAsync(customerId);
        }
        catch (ApiException ex)
        {
            throw Wrap("check existing subscriptions", ex);
        }

        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .FirstOrDefault(s =>
                string.Equals(s!.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && IsLive(s.State));
    }

    // --- Mapping & helpers -----------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.ToString().ToLowerInvariant() ?? string.Empty,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty,
    };

    private static CustomerSubscription? MapSubscription(Subscription? s)
    {
        if (s is null)
        {
            return null;
        }

        return new CustomerSubscription
        {
            Id = s.Id ?? 0,
            State = StateToString(s.State),
            PlanHandle = s.Product?.Handle ?? string.Empty,
            PlanName = s.Product?.Name ?? string.Empty,
            PriceInCents = s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0,
            Interval = s.Product?.Interval ?? 0,
            IntervalUnit = s.Product?.IntervalUnit?.ToString().ToLowerInvariant() ?? string.Empty,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextBillingAt = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
            CustomerId = s.Customer?.Id ?? 0,
            CustomerReference = s.Customer?.Reference,
            ActivatedAt = s.ActivatedAt,
            CreatedAt = s.CreatedAt,
        };
    }

    /// <summary>A subscription that is not in an end-of-life state, i.e. one that counts as "already subscribed".</summary>
    private static bool IsLive(SubscriptionState? state) => state switch
    {
        SubscriptionState.Active
            or SubscriptionState.Trialing
            or SubscriptionState.Pending
            or SubscriptionState.Assessing
            or SubscriptionState.Paused
            or SubscriptionState.PastDue
            or SubscriptionState.SoftFailure
            or SubscriptionState.Unpaid
            or SubscriptionState.AwaitingSignup => true,
        _ => false,
    };

    /// <summary>Converts the SDK's PascalCase enum name to its snake_case wire value (e.g. <c>PastDue</c> → <c>past_due</c>).</summary>
    private static string StateToString(SubscriptionState? state)
    {
        if (state is null)
        {
            return string.Empty;
        }

        var name = state.Value.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                builder.Append('_');
            }
            builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    private static (string FirstName, string LastName) ResolveName(SubscriberInfo subscriber)
    {
        var first = subscriber.FirstName?.Trim();
        var last = subscriber.LastName?.Trim();

        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
        {
            return (first, last);
        }

        var local = subscriber.Email.Contains('@', StringComparison.Ordinal)
            ? subscriber.Email[..subscriber.Email.IndexOf('@', StringComparison.Ordinal)]
            : subscriber.Email;

        if (string.IsNullOrEmpty(first))
        {
            first = string.IsNullOrWhiteSpace(local) ? "eShop" : local;
        }
        if (string.IsNullOrEmpty(last))
        {
            last = "Subscriber";
        }
        return (first, last);
    }

    private string RequireFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new SubscriptionBillingException(
                "Maxio is not configured: 'Maxio:ProductFamilyHandle' is missing. Load it into user-secrets from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }
        return _settings.ProductFamilyHandle;
    }

    private SubscriptionBillingException Wrap(string operation, ApiException ex)
    {
        _logger.LogError(ex, "Maxio API error while trying to {Operation}: HTTP {StatusCode}.", operation, ex.ResponseCode);
        return new SubscriptionBillingException($"Billing provider error while trying to {operation} (HTTP {ex.ResponseCode}).", ex);
    }
}
