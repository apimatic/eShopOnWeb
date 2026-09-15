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

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest? request, IOrderPaymentService service, HttpContext httpContext) =>
            {
                request ??= new RefundOrderRequest();
                request.OrderId = orderId;
                request.BuyerId = CreateOrderEndpoint.RequireBuyerId(httpContext);
                var headerKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(headerKey))
                {
                    request.IdempotencyKey = headerKey;
                }

                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        var refund = await service.RefundAsync(
            request.BuyerId!,
            request.OrderId,
            request.Amount,
            request.IdempotencyKey ?? string.Empty,
            default);

        return Results.Ok(new RefundOrderResponse
        {
            RefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            Status = refund.Status,
            Amount = refund.Amount
        });
    }
}
