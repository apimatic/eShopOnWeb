using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscription operations scoped to the caller of the current request.
/// </summary>
/// <remarks>
/// The subscription endpoints never accept a customer identifier from the client — who is being
/// billed is derived from the bearer token alone. Concentrating that resolution here means the
/// endpoints cannot forget to do it, and there is exactly one place to audit for the rule that a
/// caller can only ever act on their own billing records.
/// </remarks>
public interface ISubscriptionApiService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(string planHandle, string? idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The billing reference of the current caller, for echoing back in responses.</summary>
    Task<string> GetBillingReferenceAsync();
}

/// <inheritdoc />
public class SubscriptionApiService : ISubscriptionApiService
{
    private readonly ISubscriptionBillingService _billing;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private SubscriberIdentity? _subscriber;

    public SubscriptionApiService(
        ISubscriptionBillingService billing,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _billing = billing;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billing.ListPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(
        string planHandle,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var subscriber = await ResolveSubscriberAsync().ConfigureAwait(false);
        return await _billing.SubscribeAsync(subscriber, planHandle, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var subscriber = await ResolveSubscriberAsync().ConfigureAwait(false);
        return await _billing.ListSubscriptionsAsync(subscriber, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetBillingReferenceAsync()
    {
        var subscriber = await ResolveSubscriberAsync().ConfigureAwait(false);
        return subscriber.BillingReference;
    }

    /// <summary>
    /// Turns the bearer token's principal into the shopper we bill, reading the account's email from
    /// the identity store rather than trusting anything the client sent.
    /// </summary>
    private async Task<SubscriberIdentity> ResolveSubscriberAsync()
    {
        if (_subscriber is not null)
        {
            return _subscriber;
        }

        var principal = _httpContextAccessor.HttpContext?.User;
        var userName = principal?.Identity?.Name
            ?? principal?.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriberResolutionException(
                StatusCodes.Status401Unauthorized,
                "The access token does not identify a user.");
        }

        var user = await _userManager.FindByNameAsync(userName).ConfigureAwait(false);

        if (user is null)
        {
            // The token is well formed but the account behind it is gone.
            throw new SubscriberResolutionException(
                StatusCodes.Status401Unauthorized,
                "The account named by the access token no longer exists.");
        }

        var email = !string.IsNullOrWhiteSpace(user.Email)
            ? user.Email!
            : userName.Contains('@', StringComparison.Ordinal) ? userName : null;

        if (email is null)
        {
            throw new SubscriberResolutionException(
                StatusCodes.Status422UnprocessableEntity,
                "The signed-in account has no email address. Billing requires one before a subscription can be created.");
        }

        var (firstName, lastName) = SubscriberIdentity.DeriveName(userName, email);

        return _subscriber = new SubscriberIdentity(userName, email, firstName, lastName);
    }
}
