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
/// Pays for an order with PayPal, using either one-off card details or one of the caller's saved cards.
/// Idempotent: repeating the call for an already-paid order returns the existing payment, never a second charge.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService orderPaymentService, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(orderId, request, orderPaymentService, user, cancellationToken))
            .Produces<PayOrderResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        int orderId,
        PayOrderRequest request,
        IOrderPaymentService orderPaymentService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var input = new OrderPaymentInput
        {
            Card = request.Card?.ToCardDetails(),
            SavedPaymentMethodId = request.SavedPaymentMethodId
        };

        var result = await orderPaymentService.PayOrderAsync(buyerId, orderId, input, cancellationToken);

        switch (result.Outcome)
        {
            case PayOrderOutcome.Paid:
            case PayOrderOutcome.AlreadyPaid:
                return Results.Ok(new PayOrderResponse(request.CorrelationId())
                {
                    OrderId = result.Order!.Id,
                    PaymentStatus = result.Order.PaymentStatus.ToString(),
                    Order = OrderDto.FromOrder(result.Order),
                    Message = result.Outcome == PayOrderOutcome.AlreadyPaid ? "Order was already paid." : null
                });

            case PayOrderOutcome.OrderNotFound:
                return Results.Problem(detail: $"Order {orderId} was not found.", statusCode: StatusCodes.Status404NotFound);

            case PayOrderOutcome.SavedCardNotFound:
                return Results.Problem(detail: result.Error, statusCode: StatusCodes.Status404NotFound);

            case PayOrderOutcome.AlreadyRefunded:
                return Results.Problem(detail: "This order has already been refunded and cannot be paid again.", statusCode: StatusCodes.Status409Conflict);

            case PayOrderOutcome.InvalidRequest:
                return Results.Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

            case PayOrderOutcome.PaymentFailed:
                return Results.Problem(detail: result.Error, statusCode: StatusCodes.Status402PaymentRequired);

            default:
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
