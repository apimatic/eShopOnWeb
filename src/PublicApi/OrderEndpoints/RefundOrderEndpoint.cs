using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Fully refunds an order's PayPal payment. Idempotent in effect: refunding an already-refunded order
/// returns the existing refund without refunding again.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderPaymentService orderPaymentService, CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var request = new RefundOrderRequest();
                request.SetRouteAndBuyer(orderId, buyerId);
                return await HandleAsync(request, orderPaymentService, ct);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService,
        CancellationToken ct)
    {
        var result = await orderPaymentService.RefundAsync(request.BuyerId!, request.OrderId, ct);

        var failure = ApiResultMapper.MapFailure(result);
        if (failure is not null)
        {
            return failure;
        }

        var order = result.Value;
        var response = new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            RefundId = order.PaymentRefundId,
            Order = OrderDto.From(order)
        };
        return Results.Ok(response);
    }
}
