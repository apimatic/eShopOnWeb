using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns money after fulfilment, in full or in part. Carries the caller's idempotency key, so a
/// repeated request cannot refund twice while two distinct partial refunds stay legitimate.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService, HttpContext context) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService, context);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService,
        HttpContext context)
    {
        var buyerId = context.BuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new
            {
                message = "A refund needs an 'idempotencyKey', so repeating this request cannot refund twice."
            });
        }

        var refund = await paymentService.RefundAsync(buyerId, request.OrderId, request.Amount,
            request.IdempotencyKey!, context.RequestAborted);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.RefundId,
            Refund = refund
        };

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.RefundId}", response);
    }
}
