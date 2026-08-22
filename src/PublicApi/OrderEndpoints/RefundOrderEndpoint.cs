using System.Security.Claims;
using System.Threading;
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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CreateOrderEndpoint.RequireBuyerId(user);
                return await HandleAsync(request, service, cancellationToken);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
        => HandleAsync(request, orderPaymentService, CancellationToken.None);

    private async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService, CancellationToken cancellationToken)
    {
        var refund = await orderPaymentService.RefundAsync(
            new RefundOrderCommand(request.BuyerId, request.OrderId, request.IdempotencyKey, request.Amount),
            cancellationToken);

        return Results.Ok(new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.PaypalRefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status
        });
    }
}
