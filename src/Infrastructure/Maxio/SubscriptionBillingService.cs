using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <inheritdoc cref="ISubscriptionBillingService" />
public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// The Maxio customer reference is the eShopOnWeb user id, which is unique and stable.
    /// </summary>
    private const string PlansCacheKeyPrefix = "maxio:plans:";

    private static readonly TimeSpan PlansCacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Serializes subscribe/mapping mutations inside this process so a double-click can never
    /// fan out into two concurrent "ensure customer" or "create subscription" calls against
    /// Maxio. Across processes, idempotency is guaranteed by the Maxio-side uniqueness of the
    /// customer <c>reference</c> value plus the reuse of existing live subscriptions.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

    private readonly IMaxioBillingGateway _billing;
    private readonly IRepository<SubscriptionAccount> _accountRepository;
    private readonly IReadRepository<SubscriptionAccount> _accountReadRepository;
    private readonly IOptions<MaxioOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioBillingGateway billing,
        IRepository<SubscriptionAccount> accountRepository,
        IReadRepository<SubscriptionAccount> accountReadRepository,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<SubscriptionBillingService> logger)
    {
        _billing = billing;
        _accountRepository = accountRepository;
        _accountReadRepository = accountReadRepository;
        _options = options;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListAvailablePlansAsync(
        CancellationToken cancellationToken)
    {
        string familyHandle = _options.Value.ProductFamilyHandle;
        string cacheKey = PlansCacheKeyPrefix + familyHandle;

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<MaxioProduct>? cached) && cached is not null)
        {
            return cached;
        }

        var products = await _billing.ListProductsByFamilyHandleAsync(familyHandle, cancellationToken);
        var available = products
            .Where(product => product.ArchivedAt is null)
            .OrderBy(product => product.PriceInCents)
            .ToList();

        _cache.Set(cacheKey, available, PlansCacheDuration);
        return available;
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(
        string applicationUserId,
        string applicationUserEmail,
        string productHandle,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationUserEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(productHandle);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var plan = await FindPlanAsync(productHandle, cancellationToken);
            var customer = await EnsureCustomerAsync(
                applicationUserId, applicationUserEmail, firstName, lastName, cancellationToken);

            // Idempotency: never create a second subscription for the same live plan.
            var subscriptions = await _billing.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
            var existing = subscriptions.FirstOrDefault(subscription =>
                subscription.IsRenewable &&
                string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {UserId} already holds Maxio subscription {SubscriptionId} for plan {PlanHandle}.",
                    applicationUserId, existing.Id, productHandle);
                return new SubscriptionEnrollmentResult(existing, wasCreated: false);
            }

            var created = await _billing.CreateSubscriptionAsync(
                new MaxioNewSubscription
                {
                    ProductHandle = plan.Handle!,
                    CustomerId = customer.Id
                },
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({State}) for user {UserId}, plan {PlanHandle}, " +
                "price {PriceInCents} cents, next assessment {NextAssessmentAt:O}.",
                created.Id, created.State, applicationUserId, productHandle,
                created.ProductPriceInCents, created.NextAssessmentAt);

            return new SubscriptionEnrollmentResult(created, wasCreated: true);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(
        string applicationUserId,
        string applicationUserEmail,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationUserEmail);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await FindCustomerAsync(applicationUserId, cancellationToken);
            if (customer is null)
            {
                return Array.Empty<MaxioSubscription>();
            }

            var subscriptions = await _billing.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
            return subscriptions
                .OrderByDescending(subscription => subscription.CreatedAt)
                .ToList();
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<MaxioProduct> FindPlanAsync(
        string productHandle,
        CancellationToken cancellationToken)
    {
        var plans = await ListAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null || string.IsNullOrWhiteSpace(plan.Handle))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return plan;
    }

    /// <summary>
    /// Returns the Maxio customer for the user, creating it on first use. The Maxio customer
    /// <c>reference</c> is the eShopOnWeb user id, which Maxio enforces as unique, so a racing
    /// duplicate create is detected and turned into a lookup.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(
        string applicationUserId,
        string applicationUserEmail,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(applicationUserId, cancellationToken);
        if (customer is not null)
        {
            await UpsertAccountMappingAsync(applicationUserId, applicationUserEmail, customer, cancellationToken);
            return customer;
        }

        var (resolvedFirstName, resolvedLastName) =
            ResolveDisplayName(applicationUserEmail, firstName, lastName);

        try
        {
            customer = await _billing.CreateCustomerAsync(
                new MaxioNewCustomer
                {
                    FirstName = resolvedFirstName,
                    LastName = resolvedLastName,
                    Email = applicationUserEmail,
                    Reference = applicationUserId
                },
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Another request created the same reference between our lookup and this create.
            customer = await FindCustomerAsync(applicationUserId, cancellationToken);
            if (customer is null)
            {
                throw;
            }
        }

        await UpsertAccountMappingAsync(applicationUserId, applicationUserEmail, customer, cancellationToken);
        return customer;
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(
        string applicationUserId,
        CancellationToken cancellationToken)
    {
        // Local cache first...
        var local = await _accountReadRepository.FirstOrDefaultAsync(
            new SubscriptionAccountByApplicationUserIdSpecification(applicationUserId), cancellationToken);

        // ...but Maxio is the system of record: confirm the id still exists and pick up the
        // authoritative id if the local cache is stale or was reset.
        var remote = await _billing.FindCustomerByReferenceAsync(applicationUserId, cancellationToken);
        if (remote is not null && local is not null && remote.Id != local.MaxioCustomerId)
        {
            _logger.LogInformation(
                "Refreshing Maxio customer mapping for user {UserId}: {OldCustomerId} -> {NewCustomerId}.",
                applicationUserId, local.MaxioCustomerId, remote.Id);
        }

        return remote;
    }

    private async Task UpsertAccountMappingAsync(
        string applicationUserId,
        string applicationUserEmail,
        MaxioCustomer customer,
        CancellationToken cancellationToken)
    {
        var local = await _accountRepository.FirstOrDefaultAsync(
            new SubscriptionAccountByApplicationUserIdSpecification(applicationUserId), cancellationToken);

        if (local is null)
        {
            await _accountRepository.AddAsync(new SubscriptionAccount(
                applicationUserId,
                applicationUserEmail,
                customer.Id,
                applicationUserId),
                cancellationToken);
            return;
        }

        if (!string.Equals(local.ApplicationUserEmail, applicationUserEmail, StringComparison.Ordinal))
        {
            local.Update(applicationUserEmail);
            await _accountRepository.UpdateAsync(local, cancellationToken);
        }
    }

    private static (string FirstName, string LastName) ResolveDisplayName(
        string email,
        string? firstName,
        string? lastName)
    {
        var localPart = email.Split('@')[0];
        int plusIndex = localPart.IndexOf('+');
        if (plusIndex >= 0)
        {
            localPart = localPart[..plusIndex];
        }

        // Domain label (minus the TLD) used as the last-name fallback, e.g. microsoft.com -> "microsoft".
        var domainLabels = email.Split('@').Length == 2
            ? email.Split('@')[1].Split('.')
            : Array.Empty<string>();
        string domainFallback = domainLabels.Length >= 2 ? domainLabels[^2] : string.Empty;

        var tokens = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        string resolvedFirstName = string.IsNullOrWhiteSpace(firstName)
            ? Capitalize(tokens.Length > 0 ? tokens[0] : localPart)
            : firstName.Trim();

        string resolvedLastName = string.IsNullOrWhiteSpace(lastName)
            ? Capitalize(tokens.Length > 1 ? string.Join(" ", tokens.Skip(1)) : domainFallback)
            : lastName.Trim();

        if (string.IsNullOrWhiteSpace(resolvedLastName))
        {
            resolvedLastName = "Member";
        }

        return (resolvedFirstName, resolvedLastName);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
