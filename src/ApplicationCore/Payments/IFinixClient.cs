using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IFinixClient
{
    Task<FinixIdentity> CreateBuyerIdentityAsync(CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(
        FinixCreatePaymentInstrumentRequest request,
        CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> UpdatePaymentInstrumentAsync(
        string paymentInstrumentId,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CreateAuthorizationAsync(
        FinixCreateAuthorizationRequest request,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long captureAmountInCents,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixTransfer> CreateRefundAsync(
        string transferId,
        long refundAmountInCents,
        string idempotencyId,
        CancellationToken cancellationToken = default);

    Task<FinixTransfer?> FindTransferByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken = default);

    Task<FinixTransfer> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixFee>> ListFeesForTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
