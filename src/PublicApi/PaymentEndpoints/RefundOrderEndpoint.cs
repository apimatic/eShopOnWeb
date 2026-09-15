using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Refunds the captured payment for an order, in full or in part. Guarded so total refunds never
/// exceed the captured amount. The caller-supplied idempotency key makes a repeated request replay the
/// original refund. Shopper-scoped: acts only on the caller's own order.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal>
{
    private readonly IPaymentService _paymentService;
    private readonly IRepository<Order> _orderRepository;

    public RefundOrderEndpoint(IPaymentService paymentService, IRepository<Order> orderRepository)
    {
        _paymentService = paymentService;
        _orderRepository = orderRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "A caller-supplied idempotencyKey is required for refunds." });
        }
        if (request.Amount is <= 0m)
        {
            return Results.BadRequest(new { message = "Refund amount, when supplied, must be greater than zero." });
        }

        var response = new RefundOrderResponse(request.CorrelationId());
        var buyerId = CallerIdentity.GetBuyerId(user);

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null || order.BuyerId != buyerId)
        {
            throw new EntityNotFoundException($"Order {request.OrderId} was not found.");
        }

        var refund = await _paymentService.RefundOrderAsync(order, request.Amount, request.IdempotencyKey!);

        response.RefundId = refund.PayPalRefundId;
        response.OrderId = order.Id;
        response.Order = OrderDto.From(order);
        return Results.Created($"api/orders/{order.Id}/refunds/{refund.PayPalRefundId}", response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Amount to refund. Omit to refund everything still refundable.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key that makes the refund idempotent.</summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>Identifier of the refund created (PayPal refund id).</summary>
    public string? RefundId { get; set; }
    public int OrderId { get; set; }
    public OrderDto? Order { get; set; }
}
