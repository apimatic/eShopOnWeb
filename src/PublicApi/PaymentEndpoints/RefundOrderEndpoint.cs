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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — refund a captured order, full or partial. The caller supplies
/// an idempotency key; repeats under the same key never refund twice. Acts only on the caller's order.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundCommand, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundRequest request, ClaimsPrincipal user, IOrderPaymentService service,
                CancellationToken ct) =>
            {
                return await HandleAsync(
                    new RefundCommand(orderId, PaymentUser.BuyerId(user), request, ct), service);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(RefundCommand command, IOrderPaymentService service)
    {
        var request = command.Request;
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotency key is required for a refund.");
        }

        if (request.Amount is <= 0m)
        {
            return Results.BadRequest("A refund amount, when supplied, must be positive.");
        }

        var refund = await service.RefundAsync(command.OrderId, command.BuyerId, request.Amount,
            request.IdempotencyKey, command.Ct);

        // Reload to report the resulting order status.
        var orders = await service.GetOrdersForBuyerAsync(command.BuyerId, command.Ct);
        var orderStatus = string.Empty;
        foreach (var o in orders)
        {
            if (o.Id == command.OrderId)
            {
                orderStatus = o.Status.ToString();
                break;
            }
        }

        var response = new RefundResponse
        {
            RefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            OrderStatus = orderStatus
        };
        return Results.Created($"api/orders/{command.OrderId}/refunds/{refund.PayPalRefundId}", response);
    }
}

public record RefundCommand(int OrderId, string BuyerId, RefundRequest Request, CancellationToken Ct);
