using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderCommand, HttpContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext httpContext, IPaymentService paymentService) =>
            {
                var command = new RefundOrderCommand
                {
                    OrderId = orderId,
                    Amount = request.Amount,
                    AmountCents = request.AmountCents,
                    IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? request.IdempotencyId ?? string.Empty : request.IdempotencyKey
                };
                if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
                {
                    command.IdempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault()
                        ?? httpContext.Request.Headers["X-Idempotency-Key"].FirstOrDefault()
                        ?? string.Empty;
                }
                return await HandleAsync(command, httpContext, paymentService);
            })
            .Produces<RefundDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(RefundOrderCommand request, HttpContext httpContext, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.RequireBuyer(httpContext);
        var refund = await paymentService.RefundOrderAsync(buyerId, request.OrderId, request, httpContext.RequestAborted);
        return Results.Ok(refund);
    }
}
