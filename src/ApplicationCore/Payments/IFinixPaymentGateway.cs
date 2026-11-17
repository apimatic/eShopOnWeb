using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IFinixPaymentGateway
{
    Task<FinixIdentityResponse> CreateBuyerIdentityAsync(CancellationToken cancellationToken);

    Task<FinixPaymentInstrumentResponse> CreatePaymentInstrumentAsync(
        string identityId,
        CardRequest card,
        string? ownerTag,
        CancellationToken cancellationToken);

    Task<FinixPaymentInstrumentResponse> GetPaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken);

    Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken);

    Task<FinixAuthorizationResponse> CreateAuthorizationAsync(
        string sourcePaymentInstrumentId,
        int amountCents,
        string idempotencyId,
        int orderId,
        CancellationToken cancellationToken);

    Task<FinixAuthorizationResponse> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixAuthorizationResponse?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken);

    Task<FinixAuthorizationResponse> CaptureAuthorizationAsync(
        string authorizationId,
        int amountCents,
        CancellationToken cancellationToken);

    Task<FinixAuthorizationResponse> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixTransferResponse> GetTransferAsync(string transferId, CancellationToken cancellationToken);

    Task<FinixTransferResponse?> FindTransferByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken);

    Task<FinixTransferResponse> CreateRefundAsync(
        string transferId,
        int amountCents,
        string idempotencyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinixTransferResponse>> ListTransfersAsync(
        System.DateTimeOffset from,
        System.DateTimeOffset to,
        CancellationToken cancellationToken);
}
