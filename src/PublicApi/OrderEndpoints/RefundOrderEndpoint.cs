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

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService payments) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, payments);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService payments)
        => HandleAsync(request, new ClaimsPrincipal(), payments);

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService payments)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);
        var refund = await payments.RefundOrderAsync(
            request.OrderId,
            buyerId,
            request.IdempotencyKey ?? string.Empty,
            request.Amount);

        return Results.Ok(new RefundOrderResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Status = refund.PayPalRefundStatus,
            Amount = refund.Amount,
            Currency = refund.Currency
        });
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
