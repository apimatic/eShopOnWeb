using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Resolves the profile a billing customer is created with from the authenticated user name.</summary>
public interface IBillingSubscriberDirectory
{
    /// <returns>The subscriber, or null when no such user exists.</returns>
    Task<BillingSubscriber?> FindAsync(string userName, CancellationToken cancellationToken);
}
