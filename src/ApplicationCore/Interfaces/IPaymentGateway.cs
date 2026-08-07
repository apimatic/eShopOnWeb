using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). Domain code depends only on this and the
/// SDK-free models in <see cref="PaymentProcessing"/>; the concrete implementation lives in
/// Infrastructure. Every method translates provider failures into
/// <see cref="Exceptions.PaymentException"/>.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Charges a raw card for an order in one server-side flow (create + capture).</summary>
    Task<PaymentResult> ChargeCardAsync(CardPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Charges a previously saved (vaulted) card for an order.</summary>
    Task<PaymentResult> ChargeSavedCardAsync(SavedCardPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Saves (vaults) a card at the provider and returns its token plus a safe descriptor.</summary>
    Task<VaultedCard> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default);

    /// <summary>Issues a full refund of a captured payment.</summary>
    Task<RefundResult> RefundAsync(RefundPaymentRequest request, CancellationToken cancellationToken = default);
}
