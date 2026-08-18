using System.Text.Json.Serialization;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: repeating a request under the same key must not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore]
    public int OrderId { get; set; }
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public string? PayPalRefundId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: refund a captured payment, in full or in part. Deduped by the caller's idempotency key.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService service, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service, ct);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService service)
        => HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentValidationException("An idempotency key is required for a refund.");
        }

        var refund = await service.RefundOrderAsync(request.OrderId, request.Amount, request.IdempotencyKey, ct);

        var response = new RefundOrderResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            OrderId = request.OrderId
        };

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
