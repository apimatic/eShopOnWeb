using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a captured payment, in full or in part. The caller supplies an
/// idempotency key so a repeated request does not refund twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId,
             RefundOrderRequest request,
             HttpContext httpContext,
             IOrderPaymentService paymentService,
             CancellationToken cancellationToken) =>
            {
                var refund = await paymentService.RefundOrderAsync(
                    httpContext.User.GetBuyerId(),
                    orderId,
                    request,
                    cancellationToken);

                return Results.Created($"api/orders/{orderId}/refunds/{refund.RefundId}", refund);
            })
            .Produces<RefundDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
