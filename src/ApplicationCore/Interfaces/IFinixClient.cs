using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IFinixClient
{
    Task<FinixIdentity> CreateIdentityAsync(CardPaymentDetails card, CancellationToken cancellationToken);

    Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(string identityId, CardPaymentDetails card, CancellationToken cancellationToken);

    Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken);

    Task<FinixAuthorization> CreateAuthorizationAsync(
        string sourceId,
        long amountCents,
        string currency,
        string idempotencyId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken);

    Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, long captureAmountCents, CancellationToken cancellationToken);

    Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinixFee>> ListFeesForTransferAsync(string transferId, CancellationToken cancellationToken);

    Task<FinixTransfer> CreateRefundAsync(
        string transferId,
        long refundAmountCents,
        string idempotencyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
