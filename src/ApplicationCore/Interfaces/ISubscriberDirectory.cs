using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Resolves an authenticated eShopOnWeb user name into the details the billing provider needs.
/// Implemented in Infrastructure over ASP.NET Identity.
/// </summary>
public interface ISubscriberDirectory
{
    /// <summary>
    /// Returns the email address on record for a user name, or <c>null</c> when no such user exists.
    /// </summary>
    Task<SubscriberContact?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default);
}
