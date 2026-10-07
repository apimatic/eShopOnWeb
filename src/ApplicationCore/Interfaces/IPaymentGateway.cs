using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The payment provider, seen from the shop. Implementations never throw for provider failures: every
/// outcome — including "the provider did not answer" — comes back as a result the caller must record.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The ISO 4217 currency every order is charged in.</summary>
    string Currency { get; }

    /// <summary>Charges the amount now (authorise with immediate capture).</summary>
    Task<PaymentAuthorisationResult> AuthoriseAsync(PaymentAuthorisationRequest request, CancellationToken cancellationToken);

    Task<PaymentRefundResult> RefundAsync(ProviderRefundRequest request, CancellationToken cancellationToken);
}
