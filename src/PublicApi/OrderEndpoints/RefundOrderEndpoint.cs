using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, ICheckoutService checkout, CancellationToken ct) =>
            {
                var result = await checkout.RefundAsync(
                    CallerIdentity.BuyerId(user),
                    orderId,
                    request.IdempotencyKey,
                    request.Amount,
                    ct);
                return Results.Created($"api/orders/{orderId}/refunds/{result.RefundId}", new RefundOrderResponse
                {
                    RefundId = result.RefundId,
                    OrderId = result.OrderId,
                    Status = result.Status.ToString(),
                    RefundedAmount = result.RefundedAmount,
                    RemainingRefundable = result.RemainingRefundable,
                    Currency = result.Currency
                });
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, ICheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());
}
