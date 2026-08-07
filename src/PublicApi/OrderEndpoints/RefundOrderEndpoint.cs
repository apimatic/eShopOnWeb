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
/// Fully refunds an order's PayPal payment. Idempotent: repeating the call for an already-refunded
/// order returns the existing refund, never a second refund.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(orderId, orderPaymentService, user, cancellationToken))
            .Produces<RefundOrderResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        int orderId,
        IOrderPaymentService orderPaymentService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await orderPaymentService.RefundOrderAsync(buyerId, orderId, cancellationToken);

        switch (result.Outcome)
        {
            case RefundOrderOutcome.Refunded:
            case RefundOrderOutcome.AlreadyRefunded:
                return Results.Ok(new RefundOrderResponse
                {
                    OrderId = result.Order!.Id,
                    PaymentStatus = result.Order.PaymentStatus.ToString(),
                    Order = OrderDto.FromOrder(result.Order),
                    Message = result.Outcome == RefundOrderOutcome.AlreadyRefunded ? "Order was already refunded." : null
                });

            case RefundOrderOutcome.OrderNotFound:
                return Results.Problem(detail: $"Order {orderId} was not found.", statusCode: StatusCodes.Status404NotFound);

            case RefundOrderOutcome.NotPaid:
                return Results.Problem(detail: result.Error ?? "Only a paid order can be refunded.", statusCode: StatusCodes.Status409Conflict);

            case RefundOrderOutcome.RefundFailed:
                return Results.Problem(detail: result.Error, statusCode: StatusCodes.Status422UnprocessableEntity);

            default:
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
