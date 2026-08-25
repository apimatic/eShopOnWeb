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
/// Operator action: refunds a captured payment, in full or in part. The caller-supplied idempotencyKey
/// guarantees repeating the same request never refunds twice, while distinct keys allow legitimate
/// separate partial refunds of the same capture.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderFulfilmentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderFulfilmentService orderFulfilmentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orderFulfilmentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderFulfilmentService orderFulfilmentService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { message = "idempotencyKey is required." });

        if (request.IdempotencyKey.Length > 64)
            return Results.BadRequest(new { message = "idempotencyKey must be 64 characters or fewer." });

        var response = new RefundOrderResponse(request.CorrelationId());

        var (order, refund) = await orderFulfilmentService.RefundAsync(request.OrderId, request.Amount,
            request.IdempotencyKey, request.Note);

        response.RefundId = refund.PayPalRefundId;
        response.OrderId = order.Id;
        response.Status = refund.Status;
        response.Amount = refund.Amount;
        response.Order = OrderMapping.ToDto(order);
        return Results.Ok(response);
    }
}
