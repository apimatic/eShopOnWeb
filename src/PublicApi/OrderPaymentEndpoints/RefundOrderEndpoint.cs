using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// Refunds a captured payment, in full or in part. The idempotency key makes repeats safe; two
/// distinct partial refunds of the same capture remain legitimate.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundRequest request, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundRequest request, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await paymentService.RefundAsync(
            buyerId, request.OrderId, request.Amount, request.IdempotencyKey, request.NoteToPayer);

        if (!result.IsSuccess)
        {
            return result.ToProblem();
        }

        var payment = result.Value.Order.Payment!;
        var refund = result.Value.Refund;
        var response = new RefundResponse
        {
            RefundId = refund.PayPalRefundId,
            OrderId = result.Value.Order.Id,
            Amount = refund.Amount,
            Status = refund.Status,
            TotalRefunded = payment.TotalRefunded,
            RefundableRemaining = payment.RefundableRemaining,
            Payment = payment.ToResponse()
        };
        return Results.Created($"api/orders/{result.Value.Order.Id}/refunds/{refund.PayPalRefundId}", response);
    }
}
