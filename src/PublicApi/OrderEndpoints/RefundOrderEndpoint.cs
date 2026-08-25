using System;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderBody
{
    // Omit for a full refund of whatever remains uncaptured-refunded.
    public decimal? Amount { get; set; }

    // Required. Repeating a request with the same key never refunds twice; two distinct partial
    // refunds of the same capture must use two distinct keys.
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderRequest : BaseRequest
{
    public RefundOrderRequest(int orderId, string buyerId, RefundOrderBody body)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Body = body;
    }

    public int OrderId { get; }
    public string BuyerId { get; }
    public RefundOrderBody Body { get; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Refunds a fulfilled order's captured payment, in full or in part.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderBody body, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                var request = new RefundOrderRequest(orderId, user.Identity!.Name!, body);
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService)
    {
        if (string.IsNullOrWhiteSpace(request.Body.IdempotencyKey))
        {
            return Results.BadRequest("idempotencyKey is required.");
        }

        try
        {
            var refund = await paymentService.RefundOrderAsync(request.OrderId, request.BuyerId, request.Body.Amount, request.Body.IdempotencyKey);

            var response = new RefundOrderResponse(request.CorrelationId())
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                Amount = refund.Amount,
                Status = refund.Status.ToString()
            };
            return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
        }
        catch (Exception ex) when (ex is OrderNotFoundException or InvalidOrderStateException or PaymentGatewayException)
        {
            return PaymentExceptionResults.Map(ex);
        }
    }
}
