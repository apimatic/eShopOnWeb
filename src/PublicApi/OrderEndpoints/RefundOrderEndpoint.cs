using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Partial amount to refund; omit to refund everything still refundable.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: repeating the request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = "";

    public string? NoteToPayer { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public string RefundId { get; set; } = "";
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
}

/// <summary>
/// Returns after fulfilment: refunds the captured payment, in full or in part.
/// Operators may refund any order; shoppers may refund only their own.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, [FromBody] RefundOrderRequest request, HttpContext httpContext,
                IOrderPaymentService orderPaymentService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(orderId, request, httpContext, orderPaymentService, cancellationToken);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, HttpContext httpContext,
        IOrderPaymentService orderPaymentService, CancellationToken cancellationToken)
    {
        // Operators (Administrators) act on any order; shoppers only on their own.
        var isOperator = httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        var buyerId = isOperator ? null : httpContext.User.GetBuyerId();

        var refund = await orderPaymentService.RefundOrderAsync(orderId, buyerId,
            request.IdempotencyKey, request.Amount, request.NoteToPayer, cancellationToken);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.PayPalRefundId,
            OrderId = orderId,
            Amount = refund.Amount,
            Status = refund.Status
        };
        return Results.Created($"api/orders/{orderId}/refunds/{refund.PayPalRefundId}", response);
    }
}
