using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Bridges the subscription endpoints to the billing service: resolves the bearer token's principal into
/// the account billing knows about, and projects domain results onto the API's DTOs.
/// </summary>
/// <remarks>
/// The endpoints never take a user id from the request body — the caller's identity always comes from the
/// token, so one shopper cannot subscribe or read on behalf of another.
/// </remarks>
public class SubscriptionsApiService
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionsApiService(
        ISubscriptionBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await _billingService.GetPlansAsync(cancellationToken);

        return plans.Select(SubscriptionPlanDto.FromPlan).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ClaimsPrincipal principal,
        string? planHandle,
        CancellationToken cancellationToken)
    {
        var identity = await ResolveIdentityAsync(principal);
        var subscription = await _billingService.SubscribeAsync(identity, planHandle, cancellationToken);

        return SubscriptionDto.FromSubscription(subscription);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var identity = await ResolveIdentityAsync(principal);
        var subscriptions = await _billingService.GetSubscriptionsAsync(identity, cancellationToken);

        return subscriptions.Select(SubscriptionDto.FromSubscription).ToList();
    }

    private async Task<BillingCustomerIdentity> ResolveIdentityAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingException(
                BillingFailureKind.Unauthorized,
                "The access token does not identify a user.");
        }

        // Look the account up rather than trusting the token for anything but the name: it confirms the
        // user still exists and supplies the email address the provider requires.
        var user = await _userManager.FindByNameAsync(userName);

        if (user?.UserName is null)
        {
            throw new BillingException(
                BillingFailureKind.Unauthorized,
                "The authenticated user no longer exists.");
        }

        return new BillingCustomerIdentity(user.UserName, user.Email);
    }
}
