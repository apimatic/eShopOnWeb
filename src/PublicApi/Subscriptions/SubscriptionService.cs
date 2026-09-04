using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaxioBillingClient _maxio;
    private readonly MaxioOptions _settings;

    public SubscriptionService(
        UserManager<ApplicationUser> userManager,
        IMaxioBillingClient maxio,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> settings)
    {
        _userManager = userManager;
        _maxio = maxio;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(RequiredProductFamilyHandle(), cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(product => new SubscriptionPlanDto
            {
                Handle = product.Handle!,
                Name = product.Name,
                Description = product.Description,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit
            })
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var products = await _maxio.ListProductsAsync(RequiredProductFamilyHandle(), cancellationToken);
        var product = products.FirstOrDefault(x => string.Equals(x.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && x.ArchivedAt is null);
        if (product?.Handle is null)
        {
            throw new SubscriptionPlanNotFoundException();
        }

        var customerReference = BuildCustomerReference(user);
        var reference = BuildSubscriptionReference(customerReference, product.Handle);
        var subscriptionLock = SubscriptionLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var existing = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing is null)
            {
                // This also repairs subscriptions created by an older build whose
                // reference used the transient in-memory Identity ID.
                var customerSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                existing = customerSubscriptions.FirstOrDefault(x =>
                    string.Equals(x.Product?.Handle, product.Handle, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(x.State, "canceled", StringComparison.OrdinalIgnoreCase));
            }
            if (existing is not null)
            {
                return SubscriptionDto.FromMaxio(existing);
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(customer.Id, product.Handle, reference, cancellationToken);
                return SubscriptionDto.FromMaxio(created);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == 400 || exception.StatusCode == 422)
            {
                // A second app instance may win the lookup/create race. Resolve the
                // deterministic reference before surfacing a real validation error.
                var concurrentSubscription = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
                if (concurrentSubscription is not null)
                {
                    return SubscriptionDto.FromMaxio(concurrentSubscription);
                }

                throw;
            }
        }
        finally
        {
            subscriptionLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var customerReference = BuildCustomerReference(user);
        var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken)
            ?? await _maxio.FindCustomerByEmailAsync(user.Email ?? user.UserName ?? string.Empty, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(SubscriptionDto.FromMaxio).ToArray();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customerReference = BuildCustomerReference(user);
        var email = user.Email ?? user.UserName ?? $"{user.Id}@invalid.local";
        var existing = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken)
            ?? await _maxio.FindCustomerByEmailAsync(email, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var firstName = email.Split('@')[0];
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = "eShop";
        }

        try
        {
            return await _maxio.CreateCustomerAsync(
                new MaxioCreateCustomer(firstName, "Shopper", email, customerReference), cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == 400 || exception.StatusCode == 422)
        {
            // Customer references are unique in the Maxio contract. If another
            // request created it after our lookup, use that customer.
            var concurrentCustomer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken)
                ?? await _maxio.FindCustomerByEmailAsync(email, cancellationToken);
            if (concurrentCustomer is not null)
            {
                return concurrentCustomer;
            }

            throw;
        }
    }

    private async Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("The authenticated token does not contain a user identity.");
        }

        var user = await _userManager.FindByNameAsync(username);
        return user ?? throw new InvalidOperationException("The authenticated user no longer exists.");
    }

    private string RequiredProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured.");
        }

        return _settings.ProductFamilyHandle;
    }

    private static string BuildCustomerReference(ApplicationUser user)
    {
        var stableIdentity = (user.UserName ?? user.Email ?? user.Id).Trim().ToLowerInvariant();
        return $"eshop-customer-{stableIdentity}";
    }

    private static string BuildSubscriptionReference(string customerReference, string productHandle)
        => $"eshop-subscription-{customerReference}-{productHandle}";
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException() : base("The requested subscription plan is not available.")
    {
    }
}
