using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IFinixPaymentGateway
{
    Task<string> CreateBuyerIdentityAsync(string email, string firstName, string lastName,
        CancellationToken cancellationToken = default);

    Task<SavedCardResult> CreatePaymentInstrumentAsync(string identityId, CardDetails card,
        CancellationToken cancellationToken = default);

    Task DisablePaymentInstrumentAsync(string paymentInstrumentId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResult> AuthorizeAsync(string paymentInstrumentId, long amountMinor,
        string currency, string idempotencyId, string orderReference,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixCaptureResult> CaptureAsync(string authorizationId, long amountMinor,
        CancellationToken cancellationToken = default);

    Task<FinixCaptureResult> GetCaptureAsync(string transferId,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorizationResult> VoidAsync(string authorizationId,
        CancellationToken cancellationToken = default);

    Task<FinixRefundResult> RefundAsync(string captureTransferId, long amountMinor, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransactionRecord>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        string currency, CancellationToken cancellationToken = default);
}
