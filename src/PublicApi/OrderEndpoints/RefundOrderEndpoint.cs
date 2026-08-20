using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ICheckoutService>
{
    private readonly IPaymentSettings _paymentSettings;

    public RefundOrderEndpoint(IPaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, HttpContext http, ICheckoutService checkout) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CreateOrderEndpoint.RequireBuyerId(http);
                return await HandleAsync(request, checkout);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ICheckoutService checkout)
    {
        var (order, refund) = await checkout.RefundAsync(
            request.BuyerId!,
            request.OrderId,
            request.IdempotencyKey,
            request.Amount);

        return Results.Ok(new RefundOrderResponse
        {
            RefundId = refund.Id,
            OrderId = order.Id,
            Status = refund.Status,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Currency = refund.Currency,
            Order = OrderResponse.From(order, _paymentSettings.Currency)
        });
    }
}
