using System.Linq;
using System.Security.Claims;
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
/// Returns all or part of a captured payment. Idempotent per caller-supplied key.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, RefundOrderRequest? request, HttpContext httpContext, ClaimsPrincipal user,
                    IPaymentService paymentService) =>
                {
                    request ??= new RefundOrderRequest();
                    request.OrderId = orderId;
                    request.IdempotencyKey ??= httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
                    return await HandleAsync(request, user, paymentService);
                })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user,
        IPaymentService paymentService)
    {
        var refund = await paymentService.RefundOrderAsync(user.GetBuyerId(), request.OrderId,
            request.ResolveAmountCents(), request.ResolveIdempotencyKey());

        var response = new RefundOrderResponse
        {
            RefundId = refund.Id,
            Refund = PaymentMapping.ToRefundResponse(refund)
        };

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
