using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The money-moving capability the application needs, expressed in application terms. The adapter
/// that speaks the processor's dialect lives in Infrastructure, so nothing else in the app has to
/// know how a hold, a capture, a refund or a vaulted card is requested.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The currency that money is moved in, from configuration.</summary>
    string Currency { get; }

    /// <summary>
    /// Puts a hold on the amount for an order, either against a one-off card or against one of the
    /// shopper's saved cards. A repeated call under the same request id must not hold the money twice.
    /// </summary>
    Task<PaymentAuthorization> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Current state of a hold, so a stale one can be spotted before it is acted on.</summary>
    Task<PaymentAuthorization?> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Takes money that is on hold and reports the fee and the net proceeds.</summary>
    Task<CapturedPayment> CaptureAsync(string authorizationId, decimal amount, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews a hold that has gone stale and returns the replacement hold. Throws
    /// <see cref="Exceptions.PaymentProcessorException"/> when the processor will not renew it.
    /// </summary>
    Task<PaymentAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Releases a hold so the money is never taken.</summary>
    Task<PaymentAuthorization> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Returns captured money to the shopper, in full or in part.</summary>
    Task<RefundedPayment> RefundAsync(string captureId, decimal amount, string requestId, string? noteToPayer,
        CancellationToken cancellationToken = default);

    /// <summary>Saves a card with the processor and returns the token plus what is safe to show.</summary>
    Task<SavedCardToken> SaveCardAsync(CardDetails card, string shopperKey, CancellationToken cancellationToken = default);

    /// <summary>Forgets a saved card so it can no longer be used to pay.</summary>
    Task DeleteSavedCardAsync(string vaultId, string payPalCustomerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The processor's own record of transactions over a date range, covering the whole range rather
    /// than only the first page of it.
    /// </summary>
    Task<IReadOnlyList<ProcessorTransactionLine>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
