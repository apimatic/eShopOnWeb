using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IFinixPaymentGateway
{
    Task<FinixIdentityResult> CreateBuyerIdentityAsync(FinixBuyerIdentityRequest request, CancellationToken cancellationToken = default);
    Task<FinixPaymentInstrumentResult> CreatePaymentInstrumentAsync(FinixCreatePaymentInstrumentRequest request, CancellationToken cancellationToken = default);
    Task<FinixPaymentInstrumentResult> DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default);
    Task<FinixAuthorizationResult> AuthorizeAsync(FinixAuthorizationRequest request, CancellationToken cancellationToken = default);
    Task<FinixAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
    Task<FinixAuthorizationResult> CaptureAuthorizationAsync(string authorizationId, long captureAmountCents, CancellationToken cancellationToken = default);
    Task<FinixAuthorizationResult> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
    Task<FinixTransferResult> GetTransferAsync(string transferId, CancellationToken cancellationToken = default);
    Task<FinixTransferResult> RefundTransferAsync(string transferId, long refundAmountCents, CancellationToken cancellationToken = default);
    Task<FinixTransferResult> UpdateTransferTagsAsync(string transferId, IReadOnlyDictionary<string, string> tags, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinixAuthorizationResult>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinixTransferResult>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
