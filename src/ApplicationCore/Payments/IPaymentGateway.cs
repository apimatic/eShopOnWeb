using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Port to the payment processor. Every method throws
/// <see cref="Exceptions.PaymentProviderException"/> on failure; writes whose outcome could not be
/// established throw it with <see cref="Exceptions.PaymentProviderErrorKind.OutcomeUnknown"/>.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>ISO-4217 currency every amount is charged in.</summary>
    string Currency { get; }

    /// <summary>Places a hold for the amount. Replaying the same <see cref="AuthorizePaymentCommand.RequestId"/> never holds twice.</summary>
    Task<ProviderAuthorization> AuthorizeAsync(AuthorizePaymentCommand command, CancellationToken cancellationToken = default);

    Task<ProviderAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<ProviderAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken = default);

    Task<ProviderCapture> CaptureAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Re-reads the provider order to find a capture made against it (settles an unknown capture outcome).</summary>
    Task<ProviderCapture?> FindCaptureAsync(string providerOrderId, CancellationToken cancellationToken = default);

    Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default);

    Task<ProviderAuthorizationState> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    Task<ProviderRefund> RefundAsync(string captureId, decimal amount, string requestId, string customId, CancellationToken cancellationToken = default);

    Task<ProviderSavedCard> SaveCardAsync(CardDetails card, string? providerCustomerId, string requestId, CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    /// <summary>One page of the provider's own transaction record. The range must not exceed <see cref="MaxSearchRange"/>.</summary>
    Task<ProviderTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, int page, CancellationToken cancellationToken = default);

    TimeSpan MaxSearchRange { get; }
}
