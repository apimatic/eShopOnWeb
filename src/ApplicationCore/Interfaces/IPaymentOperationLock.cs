using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A claim on an order's payment operations (pay or refund), held in the application's store so that a
/// second concurrent caller is refused before it reaches the payment provider.
/// </summary>
public interface IPaymentOperationLock
{
    /// <returns><c>false</c> when another operation on the order already holds the claim.</returns>
    Task<bool> TryAcquireAsync(int orderId, CancellationToken cancellationToken);

    Task ReleaseAsync(int orderId);
}
