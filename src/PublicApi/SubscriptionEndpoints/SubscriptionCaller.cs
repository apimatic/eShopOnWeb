using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The authenticated shopper behind a subscription request, bound from the JWT. The token carries the
/// user name; the stable user id (the billing identity key) and email come from the identity store.
/// </summary>
public sealed class SubscriptionCaller
{
    private SubscriptionCaller(BillingAccount? account, CancellationToken requestAborted)
    {
        Account = account;
        RequestAborted = requestAborted;
    }

    /// <summary>Null when the token's user no longer exists.</summary>
    public BillingAccount? Account { get; }

    public CancellationToken RequestAborted { get; }

    public static async ValueTask<SubscriptionCaller> BindAsync(HttpContext context)
    {
        var userName = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return new SubscriptionCaller(null, context.RequestAborted);
        }

        var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return new SubscriptionCaller(null, context.RequestAborted);
        }

        return new SubscriptionCaller(new BillingAccount(user.Id, user.Email ?? userName), context.RequestAborted);
    }
}
