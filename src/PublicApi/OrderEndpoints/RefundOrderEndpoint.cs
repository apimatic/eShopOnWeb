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

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderApiRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderApiRequest request, ICheckoutService checkout, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = ApiUser.GetBuyerId(user);
                request.CallerIsAdministrator = ApiUser.IsAdministrator(user);
                return await HandleAsync(request, checkout);
            })
            .Produces<CreateRefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderApiRequest request, ICheckoutService checkout)
    {
        var (order, payment, refund) = await checkout.RefundAsync(
            request.BuyerId!,
            request.OrderId,
            new RefundOrderRequest
            {
                IdempotencyKey = request.IdempotencyKey ?? string.Empty,
                Amount = request.Amount
            },
            request.CallerIsAdministrator);

        var mapped = PaymentResponseMapper.MapRefund(refund);
        return Results.Created($"api/orders/{order.Id}/refunds/{mapped.RefundId}", new CreateRefundResponse
        {
            RefundId = mapped.RefundId,
            Status = mapped.Status,
            Amount = mapped.Amount,
            Currency = mapped.Currency,
            Order = PaymentResponseMapper.Map(order, payment)
        });
    }
}

public class RefundOrderApiRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
    public bool CallerIsAdministrator { get; set; }
    public string? IdempotencyKey { get; set; }
    public decimal? Amount { get; set; }
}

public class CreateRefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public OrderResponse? Order { get; set; }
}
