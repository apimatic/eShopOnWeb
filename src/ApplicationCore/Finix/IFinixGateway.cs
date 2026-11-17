using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Finix;

public interface IFinixGateway
{
    string Currency { get; }
    string MerchantId { get; }

    Task<FinixIdentity> CreateBuyerIdentityAsync(CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(
        string identityId,
        CardDetails card,
        CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> GetPaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default);

    Task DisablePaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CreateAuthorizationAsync(
        long amountCents,
        string sourcePaymentInstrumentId,
        string idempotencyId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long amountCents,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixTransfer> CreateRefundAsync(
        string parentTransferId,
        long amountCents,
        string idempotencyId,
        CancellationToken cancellationToken = default);

    Task<FinixTransfer?> FindTransferByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken = default);

    Task<FinixTransfer> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixFee>> GetFeesForTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
