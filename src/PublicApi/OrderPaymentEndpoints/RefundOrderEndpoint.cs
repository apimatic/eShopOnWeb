using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — return money after fulfilment, in full or in part, for the
/// caller's own order. Carries a caller-supplied idempotency key. Returns the new refund id as a
/// top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public RefundOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        var buyerId = CurrentUser.RequireBuyerId(_http);
        var (order, refund) = await service.RefundAsync(
            buyerId, request.OrderId, request.Amount, request.IdempotencyKey, CurrentUser.RequestAborted(_http));

        var response = new RefundOrderResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            OrderPaymentStatus = order.PaymentStatus.ToString(),
            TotalRefunded = order.TotalRefunded(),
            RefundableRemaining = order.RefundableRemaining()
        };
        return Results.Created($"api/orders/{order.Id}/refunds/{refund.Id}", response);
    }
}
