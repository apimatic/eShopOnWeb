using System;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit for a full refund of whatever remains refundable.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key making the refund idempotent. Repeating under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = "";
}

public class RefundOrderResponse : BaseResponse
{
    public string RefundId { get; set; } = "";
    public int PaymentRefundId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
    public string PayPalRefundId { get; set; } = "";
    public int OrderId { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
}

/// <summary>
/// Refunds a captured payment, in full or in part. Idempotent under the IdempotencyKey.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IRepository<Order> orderRepository, IPaymentService paymentService) =>
                await HandleAsync(orderId, request, orderRepository, paymentService))
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request) => throw new NotSupportedException();

    public async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request,
        IRepository<Order> orderRepository, IPaymentService paymentService)
    {
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null)
        {
            return Results.NotFound(new { error = $"Order {orderId} was not found." });
        }

        var refund = await paymentService.RefundOrderAsync(order, request.Amount, request.IdempotencyKey);

        return Results.Ok(new RefundOrderResponse
        {
            // Top-level identifier as required.
            RefundId = refund.PayPalRefundId,
            PaymentRefundId = refund.Id,
            Amount = refund.Amount,
            Status = refund.Status,
            PayPalRefundId = refund.PayPalRefundId,
            OrderId = order.Id,
            TotalRefunded = order.Payment?.RefundedAmount ?? refund.Amount,
            RemainingRefundable = (order.Payment?.CapturedAmount ?? 0) - (order.Payment?.RefundedAmount ?? 0)
        });
    }
}
