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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersRequest : BaseRequest
{
    public MyOrdersRequest(string buyerId)
    {
        BuyerId = buyerId;
    }

    public string BuyerId { get; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PaymentStatus { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal RefundedAmount { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's own orders with their current payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, PaymentDependencies>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user,
             IRepository<Order> orderRepository, IRepository<Payment> paymentRepository, IRepository<Buyer> buyerRepository,
             IRepository<CatalogItem> catalogItemRepository, IPayPalClient payPalClient, IOptions<PayPalOptions> payPalOptions) =>
            {
                var request = new MyOrdersRequest(user.Identity!.Name!);
                var deps = new PaymentDependencies(orderRepository, paymentRepository, buyerRepository, catalogItemRepository, payPalClient, payPalOptions.Value);
                return await HandleAsync(request, deps);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, PaymentDependencies deps)
    {
        var response = new MyOrdersResponse(request.CorrelationId());

        var orders = await deps.OrderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(request.BuyerId));
        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = orderIds.Length == 0
            ? new List<Payment>()
            : await deps.PaymentRepository.ListAsync(new PaymentsByOrderIdsSpec(orderIds));

        response.Orders = orders.Select(o =>
        {
            var payment = payments.FirstOrDefault(p => p.OrderId == o.Id);
            return new OrderSummaryDto
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                Total = o.Total(),
                Status = o.Status.ToString(),
                PaymentStatus = payment?.Status.ToString(),
                CaptureStatus = payment?.CaptureStatus,
                CapturedAmount = payment?.CapturedAmount,
                RefundedAmount = payment?.RefundedAmount ?? 0m
            };
        }).OrderByDescending(o => o.OrderDate).ToList();

        return Results.Ok(response);
    }
}
