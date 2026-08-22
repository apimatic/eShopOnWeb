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
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, HttpContext httpContext, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(orderId, request, httpContext, checkout);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderCheckoutService checkout) =>
        throw new System.NotSupportedException();

    private async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, HttpContext httpContext, IOrderCheckoutService checkout)
    {
        var buyerId = CreateOrderEndpoint.BuyerId(httpContext);
        var isAdmin = httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        var idempotencyKey = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey) &&
            httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var headerKey))
        {
            idempotencyKey = headerKey.ToString();
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CheckoutException(400, "Refunds require an idempotencyKey in the body or an Idempotency-Key header.");
        }

        var refund = await checkout.RefundAsync(orderId, buyerId, isAdmin, request.Amount, idempotencyKey);
        var order = isAdmin
            ? await checkout.GetOrderForOperatorAsync(orderId)
            : await checkout.GetOrderForBuyerAsync(orderId, buyerId);

        return Results.Ok(new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            OrderId = orderId,
            Refund = OrderResponseMapper.MapRefund(refund),
            Order = OrderResponseMapper.Map(order)
        });
    }
}
