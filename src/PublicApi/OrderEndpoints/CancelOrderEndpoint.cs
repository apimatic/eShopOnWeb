using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing any held funds by voiding
/// the PayPal authorization. No money ever moves for a cancelled order.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest,
    (IRepository<Order> Orders, IRepository<OrderPayment> Payments, IPaymentGatewayService Gateway, CancellationToken Ct)>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orders, IRepository<OrderPayment> payments, IPaymentGatewayService gateway, CancellationToken ct) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), (orders, payments, gateway, ct));
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request,
        (IRepository<Order> Orders, IRepository<OrderPayment> Payments, IPaymentGatewayService Gateway, CancellationToken Ct) dependency)
    {
        var order = await dependency.Orders.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            // Idempotent-in-effect: a repeated cancel call is a no-op.
            return Results.Ok(new CancelOrderResponse(request.CorrelationId()) { OrderId = order.Id, Status = order.Status.ToString() });
        }

        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            var paymentSpec = new OrderPaymentByOrderIdSpec(order.Id);
            var payment = await dependency.Payments.FirstOrDefaultAsync(paymentSpec);
            if (payment is not null)
            {
                await dependency.Gateway.VoidAuthorizationAsync(payment.AuthorizationId, $"eshop-void-order-{order.Id}", dependency.Ct);
                payment.RecordVoid("VOIDED");
                await dependency.Payments.UpdateAsync(payment);
            }
        }
        else if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new OrderStateException($"Cannot cancel order {order.Id} because it is in status {order.Status}.");
        }

        order.MarkCancelled();
        await dependency.Orders.UpdateAsync(order);

        return Results.Ok(new CancelOrderResponse(request.CorrelationId()) { OrderId = order.Id, Status = order.Status.ToString() });
    }
}
