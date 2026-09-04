using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: refunds a captured payment, in full or in part. A partly-refunded order
/// can never be refunded beyond what was captured. Idempotent under the caller's key.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, int, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService) =>
            {
                return await HandleAsync(orderId, request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, IPaymentService paymentService)
    {
        var result = await paymentService.RefundOrderAsync(orderId, request?.Amount, request?.IdempotencyKey ?? string.Empty);

        var response = new RefundOrderResponse(request?.CorrelationId() ?? System.Guid.NewGuid())
        {
            OrderId = result.OrderId,
            RefundId = result.RefundId,
            PayPalRefundId = result.PayPalRefundId,
            Amount = result.Amount,
            TotalRefunded = result.TotalRefunded,
            CapturedAmount = result.CapturedAmount,
            Currency = result.Currency,
            OrderStatus = result.OrderStatus
        };

        return Results.Ok(response);
    }
}