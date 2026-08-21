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

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public bool AlreadyProcessed { get; set; }
    public OrderResponse Order { get; set; } = new();
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IOrderCheckoutService checkout, ClaimsPrincipal user) =>
            {
                request ??= new RefundOrderRequest();
                var result = await checkout.RefundAsync(user.GetBuyerId(), orderId, request.Amount, request.IdempotencyKey);
                var body = new RefundOrderResponse
                {
                    RefundId = result.Refund.Id,
                    PayPalRefundId = result.Refund.PayPalRefundId,
                    Status = result.Refund.Status,
                    Amount = result.Refund.Amount,
                    Currency = result.Refund.Currency,
                    AlreadyProcessed = result.AlreadyProcessed,
                    Order = OrderResponseMapper.From(result.Order)
                };
                return result.AlreadyProcessed ? Results.Ok(body) : Results.Created($"api/orders/{orderId}/refunds/{body.RefundId}", body);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderCheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());
}
