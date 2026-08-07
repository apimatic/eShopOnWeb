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

/// <summary>
/// Refunds an order's PayPal payment in full. Idempotent: repeating the call never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public RefundOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new RefundOrderRequest(orderId), user, ct);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var order = await _orderPaymentService.RefundOrderAsync(buyerId, request.OrderId, ct);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            PayPalRefundId = order.PayPalRefundId,
            Order = order.ToDto()
        };
        return Results.Ok(response);
    }
}
