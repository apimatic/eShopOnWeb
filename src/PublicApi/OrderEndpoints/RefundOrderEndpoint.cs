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
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, HttpContext http, IOrderPaymentService payments) =>
            {
                request.OrderId = orderId;
                request.BuyerId = http.RequireBuyerId();
                return await HandleAsync(request, payments);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService payments)
    {
        var refund = await payments.RefundAsync(
            request.BuyerId,
            request.OrderId,
            new RefundOrderCommand
            {
                IdempotencyKey = request.IdempotencyKey ?? string.Empty,
                Amount = request.Amount
            },
            default);

        return Results.Ok(new RefundOrderResponse
        {
            RefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status
        });
    }
}
