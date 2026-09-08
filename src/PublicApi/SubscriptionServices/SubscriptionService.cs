using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionServices;

/// <summary>
/// Orchestrates the subscription capability against Maxio Advanced Billing (the system of
/// record). A Maxio customer is created on demand per eShopOnWeb user and is looked up by its
/// <c>reference</c> (the user's name from the JWT), which Maxio guarantees to be unique — that
/// uniqueness, plus a pre-create check for a live subscription to the same plan, makes subscribe
/// idempotent so a double-click can never create two customers or two subscriptions.
/// </summary>
public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TerminalStates = new(StringComparer.Ordinal)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private readonly IMaxioClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioClient maxio, MaxioOptions options, ILogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    /// <summary>Lists the plans (products) currently offered by the configured Maxio product family.</summary>
    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle!, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToSubscriptionPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    /// <summary>Returns all Maxio subscriptions belonging to the current user's customer.</summary>
    public async Task<IReadOnlyList<SubscriptionRecord>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var customer = await _maxio.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionRecord>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(ToSubscriptionRecord)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Subscribes the user to the requested plan. Idempotent: if the user already has a live
    /// subscription to the same plan, that subscription is returned instead of creating a new one.
    /// </summary>
    public async Task<SubscribeResult> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var availablePlans = await GetAvailablePlansAsync(cancellationToken);
        var plan = availablePlans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        // Serialize subscribe attempts per user within this process to close the race between the
        // "existing subscription?" check and the create call (cross-instance races are additionally
        // guarded by Maxio's unique customer reference).
        var gate = SubscribeGates.GetOrAdd(userName.ToLowerInvariant(), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(userName, cancellationToken);

            var existing = (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .Where(s => IsLiveSubscription(s.State))
                .FirstOrDefault(s => string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {User} already has a live subscription {SubscriptionId} to plan {PlanHandle}; returning the existing subscription.",
                    userName, existing.Id, plan.Handle);
                return new SubscribeResult(false, ToSubscriptionRecord(existing));
            }

            var created = await _maxio.CreateSubscriptionAsync(new MaxioSubscriptionInput
            {
                ProductHandle = plan.Handle,
                CustomerReference = customer.Reference,
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({State}) for user {User} on plan {PlanHandle}.",
                created.Id, created.State, userName, plan.Handle);

            return new SubscribeResult(true, ToSubscriptionRecord(created));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Creates a Maxio customer for the user if one does not already exist (idempotent).</summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(string userName, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var input = DeriveCustomerInput(userName);
        try
        {
            var created = await _maxio.CreateCustomerAsync(input, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {User}.", created.Id, userName);
            return created;
        }
        catch (MaxioApiException ex)
        {
            // Maxio enforces a single customer per reference. A concurrent signup may have won the
            // race between our lookup above and this create; in that case fall back to the lookup.
            _logger.LogInformation(ex, "Creating the Maxio customer for user {User} failed; re-looking-up by reference.", userName);
            var customer = await _maxio.FindCustomerByReferenceAsync(userName, cancellationToken);
            if (customer is null)
            {
                throw;
            }

            return customer;
        }
    }

    private MaxioCustomerInput DeriveCustomerInput(string userName)
    {
        // eShopOnWeb identities do not capture first/last names, so derive a stable, human-readable
        // name from the unique user name (an email address in this app).
        int at = userName.IndexOf('@');
        string firstName = at > 0 ? userName.Substring(0, at) : userName;
        string lastName = at > 0 && at < userName.Length - 1 ? userName.Substring(at + 1) : "User";

        return new MaxioCustomerInput
        {
            FirstName = string.IsNullOrWhiteSpace(firstName) ? userName : firstName,
            LastName = string.IsNullOrWhiteSpace(lastName) ? "User" : lastName,
            Email = userName,
            Reference = userName,
            Organization = "eShopOnWeb"
        };
    }

    private void EnsureConfigured()
    {
        string? error = _options.ConfigurationError;
        if (error is not null)
        {
            throw new MaxioConfigurationException(error);
        }
    }

    private static bool IsLiveSubscription(string? state) =>
        state is not null && !TerminalStates.Contains(state);

    private static SubscriptionPlan ToSubscriptionPlan(MaxioProduct product)
    {
        long priceInCents = product.PriceInCents ?? 0;
        return new SubscriptionPlan(
            product.Id,
            product.Handle!,
            product.Name ?? product.Handle ?? "(unnamed plan)",
            product.Description,
            priceInCents,
            CentsToDollars(priceInCents),
            product.Interval ?? 0,
            product.IntervalUnit ?? string.Empty,
            product.RequireCreditCard);
    }

    private static SubscriptionRecord ToSubscriptionRecord(MaxioSubscription subscription)
    {
        long priceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;
        var product = subscription.Product;

        return new SubscriptionRecord(
            SubscriptionId: subscription.Id,
            State: subscription.State ?? "unknown",
            Currency: string.IsNullOrWhiteSpace(subscription.Currency) ? "USD" : subscription.Currency!,
            PriceInCents: priceInCents,
            Price: CentsToDollars(priceInCents),
            ProductId: product?.Id,
            ProductHandle: product?.Handle,
            ProductName: product?.Name,
            CurrentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
            NextBillingAt: subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            CreatedAt: subscription.CreatedAt ?? DateTimeOffset.MinValue,
            CanceledAt: subscription.CanceledAt);
    }

    private static decimal CentsToDollars(long cents) => cents / 100m;
}
