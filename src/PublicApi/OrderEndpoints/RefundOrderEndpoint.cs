using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns a captured payment after fulfilment — in full or in part. The caller supplies an
/// idempotency key; repeating a request under the same key must not refund twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, service, ct);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service)
        => HandleAsync(request, user, service, default);

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user,
        IOrderPaymentService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException("A refund requires an idempotencyKey.");
        }

        var buyerId = user.BuyerId();
        var refund = await service.RefundAsync(buyerId, request.OrderId, request.Amount, request.IdempotencyKey, ct);

        // Reload the payment so the response reports the up-to-date refunded/remaining totals.
        var orders = await service.GetMyOrdersAsync(buyerId, ct);
        var payment = orders.FirstOrDefault(o => o.Order.Id == request.OrderId)?.Payment;

        var response = new RefundOrderResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            TotalRefunded = payment?.RefundedAmount() ?? refund.Amount,
            RemainingRefundable = payment?.RefundableAmount() ?? 0m,
            PaymentStatus = payment?.Status.ToString() ?? string.Empty,
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
