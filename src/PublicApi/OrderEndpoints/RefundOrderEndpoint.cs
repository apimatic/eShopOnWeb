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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a fulfilled order, in full or in part. Allowed for operators (administrators)
/// and for the shopper who owns the order. Idempotent per idempotencyKey.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, int, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentService _paymentService;

    public RefundOrderEndpoint(IRepository<Order> orderRepository, IPaymentService paymentService)
    {
        _orderRepository = orderRepository;
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RefundOrderRequest request, int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, orderId, user);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, int orderId, ClaimsPrincipal user)
    {
        var callerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(callerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        var isAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (order == null || (order.BuyerId != callerId && !isAdmin))
        {
            return Results.NotFound($"Order {orderId} was not found.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotencyKey is required so a repeated request never refunds twice.");
        }

        try
        {
            var refund = await _paymentService.RefundPaymentAsync(order, request.Amount, request.IdempotencyKey);
            var payment = await _paymentService.GetPaymentForOrderAsync(order.Id);
            var response = new RefundOrderResponse(request.CorrelationId())
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                OrderId = order.Id,
                Amount = refund.Amount,
                Status = refund.Status,
                Payment = payment == null ? null : PaymentDto.FromPayment(payment)
            };
            return Results.Ok(response);
        }
        catch (InvalidPaymentStateException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}
