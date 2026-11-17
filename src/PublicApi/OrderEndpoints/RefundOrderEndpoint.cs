using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int orderId, HttpContext http, RefundOrderRequest request, PaymentService payments) =>
                {
                    request.OrderId = orderId;
                    request.BuyerId = http.User.Identity?.Name ?? string.Empty;
                    request.RequestAborted = http.RequestAborted;
                    if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    {
                        request.IdempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
                    }
                    return await HandleAsync(request, payments);
                })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, PaymentService payments)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var refund = await payments.RefundAsync(
            request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey ?? string.Empty,
            request.RequestAborted);

        return Results.Ok(new RefundOrderResponse
        {
            RefundId = refund.RefundId,
            Refund = refund
        });
    }
}

public class RefundOrderRequest : PaymentRequestBase
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Amount to refund in currency units. Omit to refund the remaining captured balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key; the Idempotency-Key header is accepted as a fallback.</summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public string RefundId { get; set; } = string.Empty;
    public RefundView Refund { get; set; } = null!;
}
