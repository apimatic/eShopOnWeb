using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Finix;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin contract over the Finix REST API. The implementation is hand-written against
/// finix-spec/finix-openapi.yaml; no third-party Finix SDK is used.
/// </summary>
public interface IFinixClient
{
    Task<FinixIdentityResponse> CreateBuyerIdentityAsync(string? email, string? name, CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrumentResponse> CreatePaymentInstrumentAsync(
        FinixCreatePaymentInstrumentRequest request, CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrumentResponse> DisablePaymentInstrumentAsync(
        string paymentInstrumentId, CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResponse> CreateAuthorizationAsync(
        FinixCreateAuthorizationRequest request, CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResponse> GetAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResponse> CaptureAuthorizationAsync(
        string authorizationId, long captureAmountCents, CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResponse> VoidAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default);

    Task<FinixTransferResponse> GetTransferAsync(
        string transferId, CancellationToken cancellationToken = default);

    Task<FinixTransferResponse> CreateRefundAsync(
        string captureTransferId, long refundAmountCents, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransferResponse>> ListTransferReversalsAsync(
        string captureTransferId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixAuthorizationResponse>> ListAuthorizationsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransferResponse>> ListTransfersAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
