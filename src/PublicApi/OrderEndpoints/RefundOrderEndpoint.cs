using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest? request, HttpContext httpContext, IOrderPaymentService paymentService) =>
            {
                request ??= new RefundOrderRequest();
                request.OrderId = orderId;
                request.BuyerId = PaymentHttp.BuyerId(httpContext);
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    request.IdempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault()
                        ?? httpContext.Request.Headers["PayPal-Request-Id"].FirstOrDefault()
                        ?? string.Empty;
                }

                return await HandleAsync(request, paymentService);
            })
            .Produces<CreateRefundResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService)
    {
        try
        {
            var refund = await paymentService.RefundAsync(
                request.BuyerId,
                request.OrderId,
                request.IdempotencyKey,
                request.Amount);

            return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", new CreateRefundResponse
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                Status = refund.PayPalRefundStatus,
                Amount = refund.Amount,
                Currency = refund.Currency,
                OrderId = request.OrderId
            });
        }
        catch (System.Exception ex)
        {
            return PaymentHttp.FromException(ex);
        }
    }
}

public class RefundOrderRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class CreateRefundResponse
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
