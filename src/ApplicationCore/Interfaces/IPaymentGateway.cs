using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Takes and gives back money through the payment provider. Implementations never throw for a
/// provider-side failure: every outcome, including "we could not tell", comes back as a result.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The ISO currency every payment is taken in (from configuration).</summary>
    string Currency { get; }

    Task<PaymentProviderResult> AuthoriseCardPaymentAsync(CardPaymentRequest request, CancellationToken cancellationToken);

    Task<RefundProviderResult> RefundAsync(ProviderRefundRequest request, CancellationToken cancellationToken);
}
