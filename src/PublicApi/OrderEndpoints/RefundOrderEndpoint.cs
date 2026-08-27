using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a fulfilled order's captured payment, in full or in part.
/// The idempotency key guarantees a repeated request never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<ApplicationCore.Entities.PaymentAggregate.Payment> _paymentRepository;

    public RefundOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<ApplicationCore.Entities.PaymentAggregate.Payment> paymentRepository)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotencyKey is required for refunds.");
        }
        if (request.Amount.HasValue && request.Amount.Value <= 0)
        {
            return Results.BadRequest("The refund amount must be positive.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null || order.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        var refund = await paymentService.RefundPaymentAsync(order, request.Amount, request.IdempotencyKey!);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id));

        response.RefundId = refund.PayPalRefundId;
        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.RefundStatus = refund.Status;
        response.Amount = refund.Amount;
        response.TotalRefunded = payment?.TotalRefunded() ?? refund.Amount;
        response.Currency = payment?.Currency ?? string.Empty;
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>Null refunds the remaining captured amount in full.</summary>
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RefundStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal TotalRefunded { get; set; }
    public string Currency { get; set; } = string.Empty;
}
