using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRouteRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IPaymentService payments, HttpContext httpContext) =>
            {
                var route = new RefundOrderRouteRequest
                {
                    OrderId = orderId,
                    BuyerId = httpContext.User.GetBuyerId(),
                    IdempotencyKey = request.IdempotencyKey,
                    Amount = request.Amount
                };
                return await HandleAsync(route, payments);
            })
            .Produces<CreateRefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRouteRequest request, IPaymentService payments)
    {
        var result = await payments.RefundAsync(request.BuyerId, request.OrderId, request.IdempotencyKey, request.Amount);
        return Results.Created($"api/orders/{request.OrderId}/refunds/{result.RefundId}", OrderResponseMapper.From(result));
    }
}

public class RefundOrderRouteRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}
