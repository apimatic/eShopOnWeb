using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the caller's own orders with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;

    public ListMyOrdersEndpoint(IRepository<Order> orderRepository, IRepository<Payment> paymentRepository)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId));
        var payments = await _paymentRepository.ListAsync(new PaymentsWithRefundsSpec());
        var paymentsByOrder = payments
            .Where(p => string.Equals(p.BuyerId, buyerId, StringComparison.Ordinal))
            .GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.First());

        var response = new ListMyOrdersResponse();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            paymentsByOrder.TryGetValue(order.Id, out var payment);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Currency = payment?.Currency,
                Items = order.OrderItems.Select(i => new OrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Payment = payment is null ? null : new MyOrderPaymentDto
                {
                    PaymentId = payment.Id,
                    Status = payment.Status.ToString(),
                    AuthorizationId = payment.AuthorizationId,
                    AuthorizationStatus = payment.AuthorizationStatus,
                    AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
                    CaptureId = payment.CaptureId,
                    CaptureStatus = payment.CaptureStatus,
                    CapturedAmount = payment.CapturedAmount,
                    PayPalFeeAmount = payment.PayPalFeeAmount,
                    NetAmount = payment.NetAmount,
                    TotalRefunded = payment.TotalRefunded,
                    RemainingRefundable = payment.RefundableAmount,
                    Refunds = payment.Refunds.Select(r => new MyOrderRefundDto
                    {
                        RefundId = r.Id,
                        PayPalRefundId = r.PayPalRefundId,
                        Amount = r.Amount,
                        Status = r.Status,
                        CreatedAt = r.CreatedAt
                    }).ToList()
                }
            });
        }

        return Results.Ok(response);
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public MyOrderPaymentDto? Payment { get; set; }
}

public class MyOrderPaymentDto
{
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFeeAmount { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<MyOrderRefundDto> Refunds { get; set; } = new();
}

public class MyOrderRefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
