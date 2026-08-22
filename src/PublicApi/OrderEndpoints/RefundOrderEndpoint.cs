using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRouteRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderApiRequest body, HttpContext http, ICheckoutPaymentService service) =>
                await HandleAsync(new RefundOrderRouteRequest(orderId, body), http, service))
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRouteRequest request, ICheckoutPaymentService service) =>
        HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(RefundOrderRouteRequest request, HttpContext http, ICheckoutPaymentService service)
    {
        var (order, refund) = await service.RefundOrderAsync(new RefundOrderRequest
        {
            OrderId = request.OrderId,
            BuyerId = http.RequireBuyerId(),
            IdempotencyKey = request.Body.IdempotencyKey,
            Amount = request.Body.Amount
        });

        var response = new RefundOrderResponse
        {
            RefundId = refund.Id,
            OrderId = order.Id,
            Status = refund.Status,
            Amount = refund.Amount,
            PayPalRefundId = refund.PayPalRefundId
        };

        return Results.Created($"api/orders/{order.Id}/refunds/{refund.Id}", response);
    }
}

public record RefundOrderRouteRequest(int OrderId, RefundOrderApiRequest Body);
