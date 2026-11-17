using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IFinixClient
{
    Task<FinixAuthorization> CreateAuthorizationAsync(
        long amountInCents,
        string currency,
        string source,
        string idempotencyId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long captureAmountInCents,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken = default);

    Task<FinixTransfer> CreateReversalAsync(
        string transferId,
        long refundAmountInCents,
        CancellationToken cancellationToken = default);

    Task<FinixIdentity> CreateBuyerIdentityAsync(
        string email,
        string? name,
        string ownerId,
        CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> CreateCardPaymentInstrumentAsync(
        CardDetails card,
        string identityId,
        string ownerId,
        CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> DisablePaymentInstrumentAsync(
        string paymentInstrumentId,
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
