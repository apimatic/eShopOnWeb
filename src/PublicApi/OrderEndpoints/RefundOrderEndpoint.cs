using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderApiRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderApiRequest request, IOrderPaymentService service, HttpContext http) =>
            {
                request.OrderId = orderId;
                request.BuyerId = http.RequireBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<RefundApiResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderApiRequest request, IOrderPaymentService service)
    {
        var order = await service.GetBuyerOrderAsync(request.OrderId, request.BuyerId!);
        if (order == null)
        {
            return Results.NotFound();
        }

        var existing = order.FindRefundByIdempotencyKey(request.IdempotencyKey);
        if (existing != null)
        {
            return Results.Ok(new RefundApiResponse
            {
                RefundId = existing.Id,
                Refund = OrderDtoMapper.ToRefundDto(existing),
                Order = OrderDtoMapper.ToDto(order)
            });
        }

        var refund = await service.RefundAsync(new RefundOrderRequest(
            request.OrderId,
            request.BuyerId!,
            request.Amount,
            request.IdempotencyKey));

        var updated = await service.GetBuyerOrderAsync(request.OrderId, request.BuyerId!);
        var response = new RefundApiResponse
        {
            RefundId = refund.Id,
            Refund = OrderDtoMapper.ToRefundDto(refund),
            Order = updated == null ? OrderDtoMapper.ToDto(order) : OrderDtoMapper.ToDto(updated)
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
