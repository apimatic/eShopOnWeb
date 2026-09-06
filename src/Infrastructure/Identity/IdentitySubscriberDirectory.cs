using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Resolves subscribers from the ASP.NET Identity store. eShopOnWeb signs users up with their email
/// address as the user name, but the email is read from the user record rather than assumed from
/// the user name so that the two can diverge without breaking billing.
/// </summary>
public class IdentitySubscriberDirectory : ISubscriberDirectory
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentitySubscriberDirectory(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<SubscriberContact?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = user.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            // Nothing to bill to. Treated as "no such subscriber" rather than inventing an address.
            return null;
        }

        return new SubscriberContact(user.Id, user.UserName ?? userName, email);
    }
}
