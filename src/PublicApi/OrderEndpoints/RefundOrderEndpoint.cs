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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a captured payment, in full (Amount omitted) or in part. Idempotent per
/// (order, IdempotencyKey): repeating a request under the same key returns the original refund.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequestBody body, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var request = new RefundOrderRequest(user.Identity?.Name ?? string.Empty, orderId, body.Amount, body.IdempotencyKey);
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var refund = await orderPaymentService.RefundOrderAsync(request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey);

        response.RefundId = refund.Id;
        response.OrderId = request.OrderId;
        response.Refund = OrderMapper.ToRefundDto(refund);
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
