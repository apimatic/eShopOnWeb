using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Payments.Dto;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Typed client for the PayPal REST APIs used by this integration, built against the
/// OpenAPI specifications in api-specs/paypal: Checkout Orders v2, Payments v2,
/// Payment Method Tokens (Vault) v3 and Transaction Search v1.
/// </summary>
public interface IPayPalClient
{
    // Checkout Orders v2
    Task<PayPalOrderResponse> CreateOrderAsync(PayPalOrderRequest request, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalOrderResponse> AuthorizeOrderAsync(string orderId, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalOrderResponse> GetOrderAsync(string orderId, CancellationToken cancellationToken = default);

    // Payments v2
    Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
    Task<PayPalCapture> CaptureAuthorizationAsync(string authorizationId, PayPalCaptureRequest request, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalAuthorization> ReauthorizeAuthorizationAsync(string authorizationId, PayPalReauthorizeRequest request, string requestId, CancellationToken cancellationToken = default);
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default);
    Task<PayPalRefund> RefundCaptureAsync(string captureId, PayPalRefundRequest request, string requestId, CancellationToken cancellationToken = default);
    Task<PayPalRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken = default);

    // Payment Method Tokens (Vault) v3
    Task<PayPalPaymentTokenResponse> CreatePaymentTokenAsync(PayPalPaymentTokenRequest request, string requestId, CancellationToken cancellationToken = default);
    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    // Transaction Search v1
    Task<PayPalTransactionSearchResponse> SearchTransactionsAsync(DateTimeOffset startDate, DateTimeOffset endDate, int page, int pageSize, CancellationToken cancellationToken = default);
}
