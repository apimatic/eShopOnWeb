using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — refund a fulfilled (captured) order, in full or in part, under a
/// caller-supplied idempotency key. A partly-refunded order never becomes refundable beyond the captured
/// amount. Callable by the order's owner or by an administrator.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.CallerBuyerId = user.GetBuyerId();
                request.CallerIsAdmin = user.IsAdministrator();
                return await HandleAsync(request, service);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new OrderPaymentException("A refund requires a non-empty idempotencyKey.");
        }

        var outcome = await service.RefundOrderAsync(request.CallerBuyerId, request.CallerIsAdmin,
            request.OrderId, request.Amount, request.IdempotencyKey);

        var response = new RefundResponse
        {
            RefundId = outcome.RefundId,
            OrderId = request.OrderId,
            Amount = outcome.Amount,
            Status = outcome.Status,
            TotalRefunded = outcome.TotalRefunded,
            RefundableRemaining = outcome.RefundableRemaining,
            AlreadyProcessed = outcome.AlreadyProcessed
        };

        // A replayed key returns the existing refund (200); a new refund is created (201).
        return outcome.AlreadyProcessed
            ? Results.Ok(response)
            : Results.Created($"api/orders/{request.OrderId}/refunds/{outcome.RefundId}", response);
    }
}
