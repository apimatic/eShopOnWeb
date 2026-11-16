using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IFinixGateway
{
    Task<string> CreateBuyerIdentityAsync(string email, string? firstName, string? lastName, CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(string identityId, CardDetails card, CancellationToken cancellationToken = default);

    Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CreateAuthorizationAsync(long amountCents, string currency, string paymentInstrumentId,
        string orderReference, string idempotencyId, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, long captureAmountCents, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken = default);

    Task<FinixTransfer> CreateRefundAsync(string transferId, long refundAmountCents, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixFee>> ListFeesForTransferAsync(string transferId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
