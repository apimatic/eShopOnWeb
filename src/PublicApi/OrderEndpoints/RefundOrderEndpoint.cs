using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequestBody
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
}

/// <summary>
/// Refunds a fulfilled order's captured payment, in full or in part. Idempotent per caller-supplied idempotencyKey.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequestBody body, HttpContext httpContext, IOrderPaymentService paymentService) =>
            {
                var request = new RefundOrderRequest
                {
                    OrderId = orderId,
                    BuyerId = httpContext.User.Identity!.Name!,
                    Amount = body.Amount,
                    IdempotencyKey = body.IdempotencyKey
                };
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var (order, refund) = await paymentService.RefundOrderAsync(request.OrderId, request.BuyerId, request.Amount, request.IdempotencyKey);

        response.RefundId = refund.Id;
        response.OrderId = order.Id;
        response.PayPalRefundId = refund.PayPalRefundId;
        response.Amount = refund.Amount;
        response.Status = refund.Status;
        response.OrderStatus = order.Status.ToString();
        return Results.Ok(response);
    }
}
