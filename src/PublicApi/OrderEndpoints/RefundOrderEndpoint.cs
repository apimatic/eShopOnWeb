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

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IOrderCheckoutService checkout, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = BuyerIdentity.Require(user);
                return await HandleAsync(request, checkout);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderCheckoutService checkout)
    {
        var refund = await checkout.RefundAsync(
            request.BuyerId,
            request.OrderId,
            request.Amount,
            request.IdempotencyKey);

        var dto = RefundDto.From(refund);
        return Results.Created($"api/orders/{request.OrderId}/refunds/{dto.RefundId}", new RefundOrderResponse
        {
            RefundId = dto.RefundId,
            Refund = dto
        });
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public RefundDto Refund { get; set; } = new();
}
