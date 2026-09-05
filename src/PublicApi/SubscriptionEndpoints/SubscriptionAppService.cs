using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Orchestrates the subscribe flow: resolves the calling eShopOnWeb user, ensures a Maxio
/// customer exists for them (idempotent), and enrolls/lists subscriptions on their behalf.
/// </summary>
public class SubscriptionAppService : ISubscriptionAppService
{
    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionAppService(IMaxioClient maxioClient, UserManager<ApplicationUser> userManager)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await _maxioClient.ListPlansAsync(cancellationToken);
        return plans.Select(SubscriptionMapper.ToDto).ToList();
    }

    public async Task<(SubscriptionDto Subscription, bool Created)> SubscribeCurrentUserAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        var customer = await EnsureCurrentMaxioCustomerAsync(principal, cancellationToken);

        var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var alreadySubscribed = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, planHandle, System.StringComparison.OrdinalIgnoreCase) && MaxioSubscriptionStates.IsLive(s.State));

        if (alreadySubscribed is not null)
        {
            return (SubscriptionMapper.ToDto(alreadySubscribed), false);
        }

        var created = await _maxioClient.SubscribeAsync(customer.Id, planHandle, cancellationToken);
        return (SubscriptionMapper.ToDto(created), true);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetCurrentUserSubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        var customer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is null)
        {
            // User has never subscribed - no Maxio customer exists yet.
            return System.Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(SubscriptionMapper.ToDto).ToList();
    }

    private async Task<MaxioCustomer> EnsureCurrentMaxioCustomerAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        var (firstName, lastName) = SubscriptionMapper.DeriveNameFromEmail(user.Email!);
        return await _maxioClient.EnsureCustomerAsync(user.Id, user.Email!, firstName, lastName, cancellationToken);
    }

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;
        var user = userName is null ? null : await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new UserNotFoundException(userName ?? "(none)");
        }

        return user;
    }
}
