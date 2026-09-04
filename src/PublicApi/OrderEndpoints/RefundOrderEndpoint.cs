using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: refunds a captured payment, in full or in part. Repeating a request
/// under the same idempotency key must not refund twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new RefundOrderRequest(request) { OrderId = orderId }, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var (order, refund) = await paymentService.RefundAsync(
            request.OrderId, request.Amount, request.IdempotencyKey, System.Threading.CancellationToken.None);

        response.OrderId = order.Id;
        response.RefundId = refund.Id;
        response.OrderStatus = order.Status.ToString();
        response.RefundStatus = refund.Status;
        response.Amount = refund.Amount;
        response.Currency = refund.Currency;

        return Results.Ok(response);
    }
}