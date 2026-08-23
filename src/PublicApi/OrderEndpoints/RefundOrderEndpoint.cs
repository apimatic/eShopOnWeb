using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service, HttpContext httpContext) =>
            {
                var buyerId = BuyerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.OrderId = orderId;
                request.BuyerId = buyerId;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                {
                    request.IdempotencyKey = headerKey.ToString();
                }

                return await HandleAsync(request, service);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        var refund = await service.RefundAsync(request.OrderId, request.BuyerId, request.Amount, request.IdempotencyKey);
        var response = new RefundResponse(request.CorrelationId())
        {
            RefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = refund.Currency
        };

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.PayPalRefundId}", response);
    }
}
