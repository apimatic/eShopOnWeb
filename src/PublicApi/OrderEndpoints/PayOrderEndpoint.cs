using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total: PayPal places a hold on the money, nothing is taken yet.
/// Idempotent: paying an order that already has a live authorization returns its state.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService orderPaymentService, HttpContext http, CancellationToken ct) =>
            {
                return await HandleAsync(request, orderPaymentService, http, orderId, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService) =>
        HandleAsync(request, orderPaymentService, httpContext: null, orderId: 0, CancellationToken.None);

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService, HttpContext? httpContext, int orderId, CancellationToken ct)
    {
        var buyerId = httpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Card != null && request.PaymentMethodId != null)
        {
            return Results.BadRequest(new { message = "Provide either card details or a saved payment method id, not both." });
        }

        var card = request.Card == null ? null : new GatewayCard(
            request.Card.Number,
            request.Card.Expiry,
            request.Card.Cvc,
            request.Card.Name,
            request.Card.BillingAddress == null ? null : new GatewayAddress(
                request.Card.BillingAddress.AddressLine1,
                request.Card.BillingAddress.AddressLine2,
                request.Card.BillingAddress.AdminArea1,
                request.Card.BillingAddress.AdminArea2,
                request.Card.BillingAddress.PostalCode,
                request.Card.BillingAddress.CountryCode));

        var order = await orderPaymentService.PayOrderAsync(buyerId, orderId, card, request.PaymentMethodId, ct);

        return Results.Ok(new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            OrderStatus = order.Status.ToString(),
            Payment = ToPaymentState(order)
        });
    }

    internal static PaymentStateDto? ToPaymentState(Order order)
    {
        var payment = order.Payment;
        if (payment is null) return null;
        return new PaymentStateDto
        {
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            AuthorizedAmount = payment.AuthorizedAmount,
            PaymentMethodId = payment.PaymentMethodId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            RefundedAmount = payment.RefundedAmount,
            Refunds = payment.Refunds.Select(r => new RefundDto
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }
}
