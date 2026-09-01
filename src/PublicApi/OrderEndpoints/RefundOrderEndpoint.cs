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
/// Operator: refunds a fulfilled order's captured payment, in full or in part.
/// Repeating under the same idempotency key returns the original refund; distinct keys
/// issue distinct partial refunds, never beyond the captured amount.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var result = await orderPaymentService.RefundOrderAsync(request.OrderId, request.Amount,
            request.IdempotencyKey, request.Note);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = result.Refund.Id,
            PayPalRefundId = result.Refund.PayPalRefundId,
            OrderId = request.OrderId,
            Status = result.Refund.Status,
            Amount = result.Refund.Amount,
            Currency = result.Refund.Currency,
            TotalRefunded = result.Payment.TotalRefunded(),
            RemainingRefundable = result.Payment.RemainingRefundable(),
            CreatedAt = result.Refund.CreatedAt
        };
        return Results.Ok(response);
    }
}
