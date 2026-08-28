namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalClient
{
    Task<VaultedCardResult> VaultCardAsync(string merchantCustomerId, string? paypalCustomerId, CardDetails card,
        string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeCardAsync(string orderReference, decimal amount, string currency,
        CardDetails card, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> AuthorizeSavedCardAsync(string orderReference, decimal amount, string currency,
        string vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}
