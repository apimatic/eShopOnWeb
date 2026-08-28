namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalClient
{
    Task<PayPalAuthorization> AuthorizeOrderAsync(string paymentReference, decimal amount, string currency,
        CardInput? card, string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken);
    Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string requestId, CancellationToken cancellationToken);
    Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, string customId, CancellationToken cancellationToken);
    Task<PayPalRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken);
    Task<PayPalPaymentToken> CreatePaymentTokenAsync(string customerId, CardInput card,
        string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> SearchAllTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}
