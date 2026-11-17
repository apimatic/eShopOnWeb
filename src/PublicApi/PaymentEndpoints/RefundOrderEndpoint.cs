using System.Linq;
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

/// <summary>Refunds a captured order payment in full or in part.</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService, HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                var result = await paymentService.RefundOrderAsync(
                    EndpointUser.RequireUserName(httpContext),
                    EndpointUser.IsAdministrator(httpContext),
                    orderId,
                    request.ResolveAmount(),
                    request.ResolveAmountInCents(),
                    request.ResolveIdempotencyKey()
                        ?? httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault()
                        ?? httpContext.Request.Headers["X-Idempotency-Key"].FirstOrDefault(),
                    cancellationToken);

                return Results.Ok(RefundOrderResponse.From(result, request.CorrelationId()));
            })
            .Produces<RefundOrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderPaymentEndpoints");
    }
}
