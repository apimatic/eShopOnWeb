using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a fulfilled order, in full (amount omitted) or in part.
/// Idempotent on the caller-supplied idempotency key.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var refund = await paymentService.RefundPaymentAsync(
            request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey);

        response.OrderId = request.OrderId;
        response.RefundId = refund.Id;
        response.PayPalRefundId = refund.PayPalRefundId;
        response.Amount = refund.Amount;
        response.Status = refund.Status;
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int OrderId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>Partial amount; omit to refund the remaining captured amount in full.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int OrderId { get; set; }
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}
