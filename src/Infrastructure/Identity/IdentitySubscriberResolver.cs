using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Resolves the authenticated user name carried by a token into the identity billing customers are
/// keyed on.
/// </summary>
public class IdentitySubscriberResolver : ISubscriberResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentitySubscriberResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Subscriber?> ResolveAsync(string userName, CancellationToken cancellationToken = default)
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

        var email = user.Email ?? user.UserName!;

        // The billing customer reference is derived from this key, so it has to be stable for the life
        // of the account. The Identity primary key would be the natural choice, but eShopOnWeb can run
        // on the in-memory provider, where it is regenerated on every restart; the normalised user name
        // is stable in both configurations and is also what the caller's token carries.
        var userKey = user.NormalizedUserName ?? user.UserName!;

        var (firstName, lastName) = SplitName(email);

        return new Subscriber(userKey, email, firstName, lastName);
    }

    /// <summary>
    /// eShopOnWeb accounts carry no given or family name, so derive a readable pair from the local
    /// part of the email purely so the record is recognisable in the Maxio UI.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var segments = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        return segments.Length switch
        {
            0 => ("eShopOnWeb", "Shopper"),
            1 => (Capitalize(segments[0]), "Shopper"),
            _ => (Capitalize(segments[0]), Capitalize(segments[^1]))
        };
    }

    private static string Capitalize(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..];
}
