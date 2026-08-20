using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest>
{
    private readonly IOrderPaymentService _orders;

    public RefundOrderEndpoint(IOrderPaymentService orders)
    {
        _orders = orders;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, request, httpContext);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request) => Task.FromResult(Results.BadRequest());

    public async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, HttpContext httpContext)
    {
        var buyerId = PaymentRequestMapper.RequireBuyerId(httpContext);
        var refund = await _orders.RefundAsync(
            orderId,
            buyerId,
            request.IdempotencyKey,
            request.Amount,
            httpContext.RequestAborted);

        var dto = refund.ToDto();
        return Results.Created($"api/orders/{orderId}/refunds/{dto.RefundId}", new RefundOrderResponse
        {
            RefundId = dto.RefundId,
            Refund = dto
        });
    }
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public RefundDto Refund { get; set; } = new();
}
