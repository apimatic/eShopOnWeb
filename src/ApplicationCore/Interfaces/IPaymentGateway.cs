using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentGateway
{
    Task<string> CreateOrderAsync(int orderId, string paymentReference, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);

    Task<GatewayAuthorization> AuthorizeOrderAsync(string paypalOrderId, PaymentSource source,
        string requestId, CancellationToken cancellationToken);

    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);

    Task<GatewayCapture> CaptureAsync(string authorizationId, int orderId, string paymentReference, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken);

    Task<GatewayCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken);

    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);

    Task<GatewayRefund> RefundAsync(string captureId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken);

    Task<GatewaySavedCard> SaveCardAsync(PaymentCard card, string requestId,
        CancellationToken cancellationToken);

    Task DeletePaymentTokenAsync(string paymentTokenId, string requestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken);
}
