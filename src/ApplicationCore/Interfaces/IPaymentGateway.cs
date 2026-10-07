using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Takes and gives back card payments. Implementations never throw for provider failures: every outcome,
/// including "the provider did not answer", is reported in the result so it can be recorded.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>ISO 4217 code of the currency the shop charges in.</summary>
    string Currency { get; }

    /// <summary>Authorises and captures immediately. Re-sending the same idempotency key never charges twice.</summary>
    Task<PaymentAuthorisationResult> AuthoriseAsync(PaymentAuthorisationRequest request, CancellationToken cancellationToken);

    Task<RefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken);
}
