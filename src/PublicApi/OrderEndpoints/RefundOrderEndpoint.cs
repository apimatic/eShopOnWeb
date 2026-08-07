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
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? RefundId { get; set; }
    public OrderPaymentStateDto Order { get; set; } = new();
}

/// <summary>
/// Refunds the shopper's order in full. Idempotent: refunding an already-refunded order returns its
/// state without issuing a second refund.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                var request = new RefundOrderRequest { OrderId = orderId, BuyerId = user.GetBuyerId() ?? string.Empty };
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints")
            .WithSummary("Refund an order's payment in full");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var order = await service.RefundOrderAsync(request.BuyerId, request.OrderId);

        return Results.Ok(new RefundOrderResponse
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            RefundId = order.PaymentRefundId,
            Order = OrderPaymentStateDto.From(order)
        });
    }
}
