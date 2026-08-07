using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Fully refunds the signed-in shopper's paid order. Idempotent: refunding an already-refunded
/// order returns the existing result without refunding again. Partial refunds are out of scope.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var request = new RefundOrderRequest { OrderId = orderId, BuyerId = user.GetBuyerId() };
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Refunds an order's payment in full", "Fully refunds a paid order."));
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var order = await orderPaymentService.RefundOrderAsync(request.BuyerId, request.OrderId);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            PayPalRefundId = order.PayPalRefundId,
            Order = OrderDto.FromOrder(order)
        };

        return Results.Ok(response);
    }
}
