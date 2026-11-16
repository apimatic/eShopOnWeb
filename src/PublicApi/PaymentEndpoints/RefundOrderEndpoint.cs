using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Refunds a captured payment, in full or in part, idempotently.</summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, RefundOrderRequest request, HttpContext httpContext, IOrderPaymentService paymentService, ClaimsPrincipal user) =>
                {
                    request.OrderId = orderId;
                    if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    {
                        var headerKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
                        if (string.IsNullOrWhiteSpace(headerKey))
                        {
                            headerKey = httpContext.Request.Headers["X-Idempotency-Key"].ToString();
                        }

                        request.IdempotencyKey = headerKey;
                    }

                    return await HandleAsync(request, paymentService, user);
                })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService,
        ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        var refund = await paymentService.RefundAsync(buyerId!, request.OrderId, request);
        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.RefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status,
            FinixRefundTransferId = refund.FinixRefundTransferId
        };

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.RefundId}", response);
    }
}
