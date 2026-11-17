using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IFinixPaymentGateway
{
    Task<string> CreateIdentityAsync(CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(
        string finixIdentityId, CardDetails card, CancellationToken cancellationToken = default);

    Task DisablePaymentInstrumentAsync(
        string paymentInstrumentId, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CreateAuthorizationAsync(
        string paymentInstrumentId, long amountMinor, string currency,
        string orderReference, string idempotencyKey, string? securityCode,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> GetAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default);

    Task<FinixAuthorization?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId, long captureAmountMinor,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> VoidAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default);

    Task<FinixTransfer> GetTransferAsync(
        string transferId, CancellationToken cancellationToken = default);

    Task<FinixFeeSummary> GetTransferFeesAsync(
        string transferId, CancellationToken cancellationToken = default);

    Task<FinixRefund> CreateRefundAsync(
        string transferId, long amountMinor, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
