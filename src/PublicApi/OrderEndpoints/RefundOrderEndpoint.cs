using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service, HttpContext http) =>
            {
                request.OrderId = orderId;
                request.BuyerId = http.User.Identity?.Name;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        var refund = await service.RefundAsync(
            request.BuyerId ?? string.Empty,
            request.OrderId,
            request.Amount,
            request.IdempotencyKey,
            default);

        var order = await service.GetOrderForBuyerAsync(request.BuyerId ?? string.Empty, request.OrderId, default);
        return Results.Ok(new RefundOrderResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            OrderId = request.OrderId,
            Order = order == null ? null : OrderDto.From(order)
        });
    }
}
