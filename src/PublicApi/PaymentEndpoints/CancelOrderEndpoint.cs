using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — cancel before fulfilment. If a hold was taken it is voided,
/// releasing the shopper's funds so no money ever moved. Idempotent: cancelling a cancelled order is
/// a no-op; a captured order cannot be cancelled (refund it instead). Administrator only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IRepository<Order> orderRepository,
                IPaymentProcessor processor,
                CancellationToken ct) =>
            {
                var order = await orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
                if (order is null)
                {
                    return Results.NotFound(new { message = $"Order {orderId} was not found." });
                }

                if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
                {
                    return Results.Ok(PaymentMapping.ToOrderPaymentResponse(order));
                }

                switch (order.PaymentStatus)
                {
                    case OrderPaymentStatus.Authorized when order.Payment?.AuthorizationId is { } authorizationId:
                        await processor.VoidAsync(authorizationId, $"void-{order.Id}", ct);
                        order.RecordVoid();
                        break;

                    case OrderPaymentStatus.PendingPayment:
                        // No hold was ever taken, so there is nothing to release at PayPal.
                        order.CancelBeforeAuthorization();
                        break;

                    default:
                        return Results.Conflict(new { message = $"Order {orderId} cannot be cancelled in its current state ({order.PaymentStatus}). A captured order must be refunded instead." });
                }

                await orderRepository.UpdateAsync(order, ct);
                return Results.Ok(PaymentMapping.ToOrderPaymentResponse(order));
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderPaymentEndpoints");
    }
}
