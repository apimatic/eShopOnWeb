using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Finix;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IFinixClient
{
    Task<FinixIdentity> CreateBuyerIdentityAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(
        FinixCreatePaymentInstrumentRequest request,
        CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> SetPaymentInstrumentEnabledAsync(
        string paymentInstrumentId,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CreateAuthorizationAsync(
        FinixCreateAuthorizationRequest request,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long captureAmountCents,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixTransfer> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default);

    Task<FinixTransfer> CreateRefundAsync(
        string transferId,
        long refundAmountCents,
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
