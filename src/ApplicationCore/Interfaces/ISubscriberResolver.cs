using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Resolves the authenticated caller into the identity a billing customer is created for.
/// </summary>
public interface ISubscriberResolver
{
    /// <summary>
    /// Resolves <paramref name="userName"/> (the identity carried by the caller's token) into a
    /// <see cref="Subscriber"/>, or <c>null</c> when no such user exists.
    /// </summary>
    Task<Subscriber?> ResolveAsync(string userName, CancellationToken cancellationToken = default);
}
