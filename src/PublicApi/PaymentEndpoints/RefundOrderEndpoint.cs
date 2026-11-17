using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderCommand request, HttpContext httpContext, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(httpContext);
                var result = await paymentService.RefundOrderAsync(buyerId, orderId, request, cancellationToken);

                return Results.Ok(new RefundResponse
                {
                    RefundId = result.RefundId,
                    OrderId = result.OrderId,
                    FinixRefundTransferId = result.FinixRefundTransferId,
                    FinixRefundState = result.FinixRefundState,
                    RefundedAmount = result.RefundedAmount,
                    TotalRefunded = result.TotalRefunded,
                    RemainingRefundable = result.RemainingRefundable,
                    PaymentStatus = result.PaymentStatus,
                    Currency = result.Currency,
                    IdempotentReplay = result.IdempotentReplay
                });
            })
            .Produces<RefundResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
