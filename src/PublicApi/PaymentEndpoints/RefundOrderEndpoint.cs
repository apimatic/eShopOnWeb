using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
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
/// Refunds the captured payment of the caller's order, in full or in part, under a caller-supplied idempotency
/// key. Repeating a request under the same key does not refund twice; two distinct partial refunds are allowed.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.CallerName = user.Identity?.Name;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.CallerName))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentValidationException("A refund requires a caller-supplied idempotencyKey.");
        }

        var refund = await service.RefundAsync(request.CallerName, request.OrderId, request.Amount, request.IdempotencyKey!);
        var payment = await service.GetOwnedPaymentAsync(request.CallerName, request.OrderId);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            Payment = PaymentMappers.ToDto(payment)
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit to refund the full remaining captured balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Required.</summary>
    public string? IdempotencyKey { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? CallerName { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>Top-level identifier of the created refund.</summary>
    public int RefundId { get; set; }
    public OrderPaymentDto Payment { get; set; } = new();
}
