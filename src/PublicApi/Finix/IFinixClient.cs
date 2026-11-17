using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public interface IFinixClient
{
    Task<FinixIdentityResponse> CreateBuyerIdentityAsync(
        string email, string firstName, string lastName, FinixAddress address, CancellationToken cancellationToken);

    Task<FinixPaymentInstrumentResponse> CreatePaymentInstrumentAsync(
        string identityId, FinixCardInput card, CancellationToken cancellationToken);

    Task<FinixPaymentInstrumentResponse> DisablePaymentInstrumentAsync(
        string paymentInstrumentId, CancellationToken cancellationToken);

    Task<FinixAuthorizationResponse> CreateAuthorizationAsync(
        long amountCents, string currency, string paymentInstrumentId, string idempotencyId, int orderId,
        CancellationToken cancellationToken);

    Task<FinixAuthorizationResponse?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId, CancellationToken cancellationToken);

    Task<FinixAuthorizationResponse> CaptureAuthorizationAsync(
        string authorizationId, long captureAmountCents, CancellationToken cancellationToken);

    Task<FinixAuthorizationResponse> VoidAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken);

    Task<FinixTransferResponse> GetTransferAsync(
        string transferId, CancellationToken cancellationToken);

    Task<FinixTransferResponse> CreateRefundAsync(
        string captureTransferId, long amountCents, string idempotencyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinixTransferResponse>> ListTransfersAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinixAuthorizationResponse>> ListAuthorizationsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
