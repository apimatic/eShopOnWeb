using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class BillingUserResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public BillingUserResolver(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<BillingUser> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "The authenticated user identity is incomplete.");
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user == null || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "A verified account email is required for billing.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new BillingUser(user.Id, user.Email, "eShopOnWeb", "Customer");
    }
}

