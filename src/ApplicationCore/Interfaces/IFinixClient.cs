using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IFinixClient
{
    Task<Payments.FinixIdentityResponse> CreateIdentityAsync(Payments.FinixCreateIdentityRequest request, CancellationToken cancellationToken = default);
    Task<Payments.FinixPaymentInstrumentResponse> CreatePaymentInstrumentAsync(Payments.FinixCreatePaymentInstrumentRequest request, CancellationToken cancellationToken = default);
    Task<Payments.FinixPaymentInstrumentResponse> DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default);
    Task<Payments.FinixAuthorizationResponse> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
    Task<Payments.FinixAuthorizationResponse> CreateAuthorizationAsync(Payments.FinixCreateAuthorizationRequest request, CancellationToken cancellationToken = default);
    Task<Payments.FinixAuthorizationResponse> CaptureAuthorizationAsync(string authorizationId, long captureAmountCents, CancellationToken cancellationToken = default);
    Task<Payments.FinixAuthorizationResponse> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
    Task<Payments.FinixTransferResponse> CreateReversalAsync(string transferId, long refundAmountCents, CancellationToken cancellationToken = default);
    Task<Payments.FinixTransferResponse> GetTransferAsync(string transferId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Payments.FinixAuthorizationResponse>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Payments.FinixFeeResponse>> ListFeesAsync(string linkedTo, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Payments.FinixTransferResponse>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
