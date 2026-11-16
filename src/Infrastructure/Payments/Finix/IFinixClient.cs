using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Finix;

public interface IFinixClient
{
    Task<FinixIdentity> CreateBuyerIdentityAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(string identityId, string buyerId, CardDetails card,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CreateAuthorizationAsync(string sourcePaymentInstrumentId, long amountCents,
        string? securityCode, string idempotencyId, IReadOnlyDictionary<string, string> tags,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, long amountCents,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixFee>> ListFeesForTransferAsync(string transferId,
        CancellationToken cancellationToken = default);

    Task<FinixTransfer> CreateRefundAsync(string captureTransferId, long amountCents,
        CancellationToken cancellationToken = default);

    Task DisablePaymentInstrumentAsync(string paymentInstrumentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
