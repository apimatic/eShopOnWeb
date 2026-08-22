using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRouteRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest body, IOrderPaymentService orders, HttpContext http) =>
            {
                return await HandleAsync(new RefundOrderRouteRequest
                {
                    OrderId = orderId,
                    BuyerId = CreateOrderEndpoint.RequireUserName(http.User),
                    Body = body ?? new RefundOrderRequest()
                }, orders);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRouteRequest request, IOrderPaymentService orders)
    {
        var refund = await orders.RefundAsync(
            request.OrderId,
            request.BuyerId,
            request.Body.IdempotencyKey,
            request.Body.Amount);

        var dto = RefundDto.From(refund);
        return Results.Created($"api/orders/{request.OrderId}/refunds/{dto.RefundId}", new RefundOrderResponse
        {
            RefundId = dto.RefundId,
            Refund = dto
        });
    }
}

public class RefundOrderRouteRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public RefundOrderRequest Body { get; set; } = new();
}

public class RefundOrderRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public RefundDto Refund { get; set; } = new();
}
