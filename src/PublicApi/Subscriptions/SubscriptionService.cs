using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDetails?> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityDb;
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;

    public SubscriptionService(
        UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityDb,
        IMaxioClient maxioClient,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> options)
    {
        _userManager = userManager;
        _identityDb = identityDb;
        _maxioClient = maxioClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        EnsureProductFamilyConfigured();
        var products = await _maxioClient.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products.Select(ToPlan).ToList();
    }

    public async Task<SubscriptionDetails?> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            return null;

        var user = await FindUserAsync(principal);
        if (user is null || string.IsNullOrWhiteSpace(user.Id) || string.IsNullOrWhiteSpace(user.Email))
            return null;

        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(candidate => string.Equals(candidate.Handle, planHandle.Trim(), StringComparison.Ordinal));
        if (plan is null)
            return null;

        var lockKey = $"{user.Id}\u001f{plan.Handle}";
        var gate = SubscriptionLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeUnderLockAsync(user, plan, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(principal);
        if (user is null)
            return Array.Empty<SubscriptionDetails>();

        var customer = await FindExistingCustomerAsync(user, cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDetails>();

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToDetails).ToList();
    }

    private async Task<SubscriptionDetails?> SubscribeUnderLockAsync(ApplicationUser user, SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        var reference = CreateSubscriptionReference(user.Id, plan.Handle);
        var existingLink = await _identityDb.MaxioSubscriptionLinks
            .SingleOrDefaultAsync(link => link.UserId == user.Id && link.ProductHandle == plan.Handle, cancellationToken);

        if (existingLink is not null)
        {
            var existing = await _maxioClient.FindSubscriptionByReferenceAsync(existingLink.Reference, cancellationToken);
            if (existing is not null)
            {
                await CompleteLinkAsync(existingLink, existing, cancellationToken);
                return ToDetails(existing);
            }

            // A pending reservation is intentionally not reused to create another subscription.
            // A retry can recover it through the reference lookup once Maxio finishes processing.
            if (existingLink.MaxioSubscriptionId is null)
                throw new SubscriptionInProgressException();

            _identityDb.MaxioSubscriptionLinks.Remove(existingLink);
            await _identityDb.SaveChangesAsync(cancellationToken);
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken)
            ?? throw new InvalidOperationException("The Maxio customer could not be created.");

        var recovered = await _maxioClient.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (recovered is not null)
        {
            var recoveredLink = new MaxioSubscriptionLink
            {
                UserId = user.Id,
                MaxioCustomerId = customer.Id,
                MaxioSubscriptionId = recovered.Id,
                ProductHandle = plan.Handle,
                Reference = reference,
                CreatedAtUtc = DateTime.UtcNow
            };
            _identityDb.MaxioSubscriptionLinks.Add(recoveredLink);
            await SaveLinkRecoveringDuplicateAsync(recoveredLink, cancellationToken);
            return ToDetails(recovered);
        }

        var reservation = new MaxioSubscriptionLink
        {
            UserId = user.Id,
            MaxioCustomerId = customer.Id,
            ProductHandle = plan.Handle,
            Reference = reference,
            CreatedAtUtc = DateTime.UtcNow
        };
        _identityDb.MaxioSubscriptionLinks.Add(reservation);
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _identityDb.Entry(reservation).State = EntityState.Detached;
            throw new SubscriptionInProgressException();
        }

        try
        {
            var created = await _maxioClient.CreateSubscriptionAsync(plan.Handle, customer.Id, reference, cancellationToken);
            reservation.MaxioSubscriptionId = created.Id;
            await _identityDb.SaveChangesAsync(cancellationToken);
            return ToDetails(created);
        }
        catch (MaxioApiException)
        {
            // A provider response means the request was not accepted. Recover first in case
            // the provider rejected a duplicate reference, then release the local reservation.
            var recoveredAfterFailure = await _maxioClient.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (recoveredAfterFailure is not null)
            {
                await CompleteLinkAsync(reservation, recoveredAfterFailure, cancellationToken);
                return ToDetails(recoveredAfterFailure);
            }

            _identityDb.MaxioSubscriptionLinks.Remove(reservation);
            await _identityDb.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task<MaxioCustomer?> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var link = await _identityDb.MaxioCustomerLinks.FindAsync(new object[] { user.Id }, cancellationToken);
        if (link is not null)
        {
            var customer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
            if (customer is not null)
                return customer;
        }

        var existing = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (existing is not null)
        {
            await SaveCustomerLinkAsync(user.Id, existing.Id, cancellationToken);
            return existing;
        }

        try
        {
            var created = await _maxioClient.CreateCustomerAsync(
                "eShopOnWeb",
                GetLastName(user.Email!),
                user.Email!,
                user.Id,
                cancellationToken);
            await SaveCustomerLinkAsync(user.Id, created.Id, cancellationToken);
            return created;
        }
        catch (MaxioApiException exception) when ((int)exception.StatusCode == 422)
        {
            // Customer references are unique in the Maxio contract. A concurrent create
            // therefore becomes an idempotent lookup instead of a duplicate customer.
            var concurrentCustomer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
            if (concurrentCustomer is null)
                throw;

            await SaveCustomerLinkAsync(user.Id, concurrentCustomer.Id, cancellationToken);
            return concurrentCustomer;
        }
    }

    private async Task<MaxioCustomer?> FindExistingCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var link = await _identityDb.MaxioCustomerLinks.FindAsync(new object[] { user.Id }, cancellationToken);
        var customer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is not null && link is null)
            await SaveCustomerLinkAsync(user.Id, customer.Id, cancellationToken);

        return customer;
    }

    private async Task SaveCustomerLinkAsync(string userId, int customerId, CancellationToken cancellationToken)
    {
        var existing = await _identityDb.MaxioCustomerLinks.FindAsync(new object[] { userId }, cancellationToken);
        if (existing is null)
        {
            _identityDb.MaxioCustomerLinks.Add(new MaxioCustomerLink
            {
                UserId = userId,
                MaxioCustomerId = customerId,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            existing.MaxioCustomerId = customerId;
        }

        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _identityDb.ChangeTracker.Clear();
        }
    }

    private async Task CompleteLinkAsync(MaxioSubscriptionLink link, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        link.MaxioSubscriptionId = subscription.Id;
        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveLinkRecoveringDuplicateAsync(MaxioSubscriptionLink link, CancellationToken cancellationToken)
    {
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _identityDb.ChangeTracker.Clear();
        }
    }

    private async Task<ApplicationUser?> FindUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(username) ? null : await _userManager.FindByNameAsync(username);
    }

    private void EnsureProductFamilyConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
    }

    private static string CreateSubscriptionReference(string userId, string productHandle) =>
        $"eshoponweb:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{productHandle}"))).ToLowerInvariant()}";

    private static string GetLastName(string email)
    {
        var localPart = email.Split('@', 2)[0];
        return string.IsNullOrWhiteSpace(localPart) ? "Shopper" : localPart[..Math.Min(localPart.Length, 100)];
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionDetails ToDetails(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        return new SubscriptionDetails
        {
            Id = subscription.Id,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Interval = product?.Interval ?? 0,
            IntervalUnit = product?.IntervalUnit ?? string.Empty,
            State = subscription.State,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }
}

public sealed class SubscriptionInProgressException : Exception
{
    public SubscriptionInProgressException()
        : base("Subscription creation is already in progress for this plan.")
    {
    }
}
