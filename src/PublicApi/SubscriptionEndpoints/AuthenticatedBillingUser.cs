using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IAuthenticatedBillingUser
{
    Task<BillingUser> GetAsync(CancellationToken cancellationToken);
}

public sealed class AuthenticatedBillingUser(
    IHttpContextAccessor httpContextAccessor,
    UserManager<ApplicationUser> userManager) : IAuthenticatedBillingUser
{
    public async Task<BillingUser> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = httpContextAccessor.HttpContext?.User;
        var userName = principal?.Identity?.Name;
        if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingException(System.Net.HttpStatusCode.Unauthorized, "An authenticated user is required.");
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new BillingException(System.Net.HttpStatusCode.Unauthorized, "The authenticated user no longer exists.");
        }

        var email = user.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BillingException(System.Net.HttpStatusCode.UnprocessableEntity, "An account email is required to subscribe.");
        }

        var localPart = email.Split('@', 2, StringSplitOptions.TrimEntries)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "Shopper" : localPart;
        return new BillingUser(user.Id, email, firstName, "eShop");
    }
}
