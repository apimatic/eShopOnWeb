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
            async (int orderId, RefundOrderRequest request, HttpContext http, IOrderCheckoutService checkout) =>
            {
                var idempotencyKey = request.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey)
                    && http.Request.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                {
                    idempotencyKey = headerKey.ToString();
                }

                var (order, refund) = await checkout.RefundAsync(http.User.GetBuyerId(), orderId, idempotencyKey, request.Amount);
                return Results.Ok(new RefundOrderResponse
                {
                    RefundId = refund.PayPalRefundId,
                    Refund = RefundResponse.From(refund),
                    Order = OrderResponse.From(order, order.Currency ?? string.Empty)
                });
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderCheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public RefundResponse Refund { get; set; } = new();
    public OrderResponse Order { get; set; } = new();
}
