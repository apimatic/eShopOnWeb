using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Finix;

public interface IFinixClient
{
    Task<FinixIdentityResponse> CreateBuyerIdentityAsync(string? email, string? name, CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrumentResponse> CreatePaymentInstrumentAsync(FinixCardRequest card,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResponse> CreateAuthorizationAsync(long amountCents, string currency, string source,
        string idempotencyId, IReadOnlyDictionary<string, string>? tags, CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResponse> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResponse> CaptureAuthorizationAsync(string authorizationId, long captureAmountCents,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResponse> VoidAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixTransferResponse> GetTransferAsync(string transferId,
        CancellationToken cancellationToken = default);

    Task<FinixTransferResponse> RefundTransferAsync(string transferId, long refundAmountCents,
        string idempotencyId, IReadOnlyDictionary<string, string>? tags, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransferResponse>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixAuthorizationResponse>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
