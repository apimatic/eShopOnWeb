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
/// Refunds a fulfilled order, in full or in part, for the signed-in shopper. The
/// caller-supplied idempotency key makes repeating a refund request a no-op, while two
/// distinct partial refunds remain legitimate. Returns the refund id.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderCommand, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService) =>
                await HandleAsync(new RefundOrderCommand(orderId, request), paymentService))
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderCommand command, IPaymentService paymentService)
    {
        var buyerId = CallerIdentity.BuyerId(_httpContextAccessor.HttpContext!);
        var body = command.Body ?? new RefundOrderRequest(null, null);

        if (string.IsNullOrWhiteSpace(body.IdempotencyKey))
        {
            throw new PaymentException("A refund requires a caller-supplied idempotency key.", 400);
        }

        var refund = await paymentService.RefundAsync(buyerId, command.OrderId, body.Amount, body.IdempotencyKey!);

        // Re-read the payment so the response reflects the updated refund totals.
        var view = await paymentService.GetOrdersForBuyerAsync(buyerId);
        var payment = System.Linq.Enumerable.FirstOrDefault(view, v => v.Order.Id == command.OrderId)?.Payment;

        var response = new RefundResponse(
            refund.RefundId,
            new RefundDto(refund.RefundId, refund.Amount, refund.Status),
            PaymentMapper.ToDto(payment)!);

        return Results.Created($"api/orders/{command.OrderId}/refunds/{refund.RefundId}", response);
    }
}
