using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing as the system of record.
///
/// Identity model: the eShopOnWeb username carried in the JWT is used as the Maxio
/// customer <c>reference</c>, so a buyer maps to exactly one Maxio customer. Each
/// subscription is created with an app-controlled <c>reference</c>
/// (<c>eshop:{buyerId}:{planHandle}</c>), which makes subscribe operations idempotent:
/// lookups by reference come before creation, and duplicate-reference rejections from
/// Maxio are resolved by reading the winner back.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string SubscriptionReferencePrefix = "eshop";
    private const string PaymentCollectionMethod = "remittance";
    private static readonly string[] TerminalStates = { "canceled", "expired", "failed_to_cancel" };

    // Serializes subscribe operations per buyer within this process, closing the
    // double-click race on top of the reference-based idempotency above.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BuyerLocks = new();

    private readonly IMaxioApiClient _maxio;
    private readonly IRepository<MaxioCustomerLink> _linkRepository;
    private readonly IRepository<MaxioSubscriptionRecord> _recordRepository;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;
    private readonly string _productFamilyHandle;

    public MaxioSubscriptionService(
        IMaxioApiClient maxio,
        IRepository<MaxioCustomerLink> linkRepository,
        IRepository<MaxioSubscriptionRecord> recordRepository,
        IAppLogger<MaxioSubscriptionService> logger,
        IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _linkRepository = linkRepository;
        _recordRepository = recordRepository;
        _logger = logger;
        _productFamilyHandle = options.Value.ProductFamilyHandle;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireFamilyHandle();
        var products = await _maxio.ListProductsAsync(cancellationToken);

        return products
            .Where(p => p.ArchivedAt == null)
            .Where(p => string.Equals(p.ProductFamily?.Handle, familyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(p => new SubscriptionPlan(
                p.Handle ?? string.Empty,
                p.Name ?? string.Empty,
                p.Description ?? string.Empty,
                p.PriceInCents,
                p.Interval,
                p.IntervalUnit ?? string.Empty,
                p.ProductFamily?.Handle ?? string.Empty,
                p.RequireCreditCard))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string buyerId, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new ArgumentException("A buyer identity is required.", nameof(buyerId));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A subscription plan handle is required.", nameof(planHandle));
        }

        var plan = (await ListPlansAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var buyerLock = BuyerLocks.GetOrAdd(buyerId, _ => new SemaphoreSlim(1, 1));
        await buyerLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureMaxioCustomerAsync(buyerId, cancellationToken);

            var baseReference = BuildSubscriptionReference(buyerId, plan.Handle);
            var existing = await _maxio.FindSubscriptionByReferenceAsync(baseReference, cancellationToken);

            if (existing != null && !IsTerminal(existing.State))
            {
                _logger.LogInformation(
                    "Buyer {BuyerId} already has subscription {SubscriptionId} (state {State}); returning it idempotently.",
                    buyerId, existing.Id, existing.State);
                await UpsertRecordAsync(buyerId, existing, cancellationToken);
                return new SubscribeResult(ToDetails(existing), wasCreated: false);
            }

            // Resubscribing to a canceled/expired subscription: the reference is already
            // taken, so a fresh subscription gets a uniquely suffixed reference.
            var reference = existing == null
                ? baseReference
                : $"{baseReference}:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

            var created = await _maxio.CreateSubscriptionAsync(new CreateMaxioSubscriptionBody
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                Reference = reference,
                PaymentCollectionMethod = PaymentCollectionMethod
            }, cancellationToken);

            await UpsertRecordAsync(buyerId, created, cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({PlanHandle}) for buyer {BuyerId}.",
                created.Id, plan.Handle, buyerId);

            return new SubscribeResult(ToDetails(created), wasCreated: true);
        }
        finally
        {
            buyerLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var link = await _linkRepository.FirstOrDefaultAsync(new MaxioCustomerLinkByBuyerIdSpec(buyerId), cancellationToken);
        if (link == null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(link.MaxioCustomerId, cancellationToken);

        foreach (var subscription in subscriptions)
        {
            await UpsertRecordAsync(buyerId, subscription, cancellationToken);
        }

        return subscriptions.Select(ToDetails).ToList();
    }

    /// <summary>
    /// Guarantees a Maxio customer exists for the buyer, idempotently. The buyer id is
    /// the customer reference, so Maxio itself enforces the one-customer-per-buyer rule.
    /// </summary>
    private async Task<MaxioCustomer> EnsureMaxioCustomerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(buyerId, cancellationToken);
        if (existing != null)
        {
            await UpsertLinkAsync(buyerId, existing.Id, cancellationToken);
            return existing;
        }

        var (firstName, lastName) = DeriveCustomerName(buyerId);
        try
        {
            var created = await _maxio.CreateCustomerAsync(new CreateMaxioCustomerBody
            {
                FirstName = firstName,
                LastName = lastName,
                Email = buyerId,
                Reference = buyerId
            }, cancellationToken);

            await UpsertLinkAsync(buyerId, created.Id, cancellationToken);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Lost a race: another request created the customer with this reference first.
            _logger.LogInformation("Maxio rejected customer creation for reference {BuyerId}; reading it back. ({Body})",
                buyerId, ex.ResponseBody);
            var winner = await _maxio.FindCustomerByReferenceAsync(buyerId, cancellationToken)
                ?? throw ex;
            await UpsertLinkAsync(buyerId, winner.Id, cancellationToken);
            return winner;
        }
    }

    private async Task UpsertLinkAsync(string buyerId, long maxioCustomerId, CancellationToken cancellationToken)
    {
        var link = await _linkRepository.FirstOrDefaultAsync(new MaxioCustomerLinkByBuyerIdSpec(buyerId), cancellationToken);
        if (link == null)
        {
            await _linkRepository.AddAsync(new MaxioCustomerLink(buyerId, maxioCustomerId), cancellationToken);
        }
        else if (link.MaxioCustomerId != maxioCustomerId)
        {
            link.UpdateCustomerId(maxioCustomerId);
            await _linkRepository.UpdateAsync(link, cancellationToken);
        }
    }

    private async Task UpsertRecordAsync(string buyerId, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        if (subscription.Product == null)
        {
            return;
        }

        var spec = new MaxioSubscriptionRecordsByBuyerIdSpec(buyerId);
        var records = await _recordRepository.ListAsync(spec, cancellationToken);
        var record = records.FirstOrDefault(r => r.MaxioSubscriptionId == subscription.Id);

        if (record == null)
        {
            await _recordRepository.AddAsync(new MaxioSubscriptionRecord(
                buyerId,
                subscription.Customer?.Id ?? 0,
                subscription.Id,
                subscription.Reference ?? string.Empty,
                subscription.Product.Handle ?? string.Empty,
                subscription.Product.Name ?? string.Empty,
                subscription.State ?? string.Empty,
                subscription.ProductPriceInCents,
                subscription.Product.Interval,
                subscription.Product.IntervalUnit ?? string.Empty,
                NextBillingAt(subscription),
                subscription.ActivatedAt),
                cancellationToken);
        }
        else
        {
            record.UpdateFrom(
                subscription.Customer?.Id ?? record.MaxioCustomerId,
                subscription.State ?? record.State,
                subscription.ProductPriceInCents,
                NextBillingAt(subscription),
                subscription.Product.Name ?? record.PlanName);
            await _recordRepository.UpdateAsync(record, cancellationToken);
        }
    }

    private static DateTimeOffset? NextBillingAt(MaxioSubscription subscription) =>
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt;

    private static SubscriptionDetails ToDetails(MaxioSubscription subscription)
    {
        var product = subscription.Product ?? new MaxioProduct();
        return new SubscriptionDetails(
            subscription.Id,
            subscription.Customer?.Id ?? 0,
            subscription.Reference ?? string.Empty,
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            subscription.State ?? string.Empty,
            subscription.ProductPriceInCents,
            product.Interval,
            product.IntervalUnit ?? string.Empty,
            NextBillingAt(subscription),
            subscription.ActivatedAt);
    }

    private static bool IsTerminal(string? state) =>
        state != null && TerminalStates.Contains(state, StringComparer.OrdinalIgnoreCase);

    private string BuildSubscriptionReference(string buyerId, string planHandle) =>
        $"{SubscriptionReferencePrefix}:{buyerId}:{planHandle}";

    private string RequireFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_productFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio integration is not configured: 'Maxio:ProductFamilyHandle' is missing.");
        }

        return _productFamilyHandle;
    }

    /// <summary>
    /// Maxio requires first/last name on customer creation; eShopOnWeb identities are
    /// plain email addresses, so derive stable display names from the local part.
    /// </summary>
    private static (string FirstName, string LastName) DeriveCustomerName(string buyerId)
    {
        var localPart = buyerId.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var firstName = Capitalize(parts.FirstOrDefault()) ?? "eShop";
        var lastName = parts.Length > 1 ? Capitalize(parts[^1]) ?? "Customer" : "Customer";
        return (firstName, lastName);
    }

    private static string? Capitalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
