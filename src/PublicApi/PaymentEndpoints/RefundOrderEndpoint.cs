using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST api/orders/{orderId}/refunds — fully refunds an order's PayPal payment. Idempotent in
/// effect: refunding an already-refunded order returns its state unchanged (partial refunds are
/// out of scope).
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                ClaimsPrincipal user,
                IOrderPaymentService orderPaymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var order = await orderPaymentService.RefundOrderAsync(buyerId, orderId, cancellationToken);
                if (order is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(new RefundOrderResponse
                {
                    OrderId = order.Id,
                    PaymentStatus = order.PaymentStatus.ToString(),
                    RefundId = order.PayPalRefundId
                });
            })
            .Produces<RefundOrderResponse>()
            .WithTags("PaymentEndpoints");
    }
}
