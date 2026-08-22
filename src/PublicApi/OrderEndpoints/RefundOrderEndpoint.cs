using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderCheckoutService checkoutService, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, checkoutService);
            })
            .Produces<CreateRefundResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderCheckoutService checkoutService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new CheckoutException("idempotencyKey is required.", 400);
        }

        var refund = await checkoutService.RefundAsync(
            request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey, default);

        return Results.Ok(new CreateRefundResponse
        {
            RefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Currency = refund.Currency,
            Status = refund.Status
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

public class CreateRefundResponse : BaseResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
