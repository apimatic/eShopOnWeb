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
/// Lists the caller's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;

    public MyOrdersEndpoint(IRepository<Order> orderRepository, IRepository<OrderPayment> paymentRepository)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) => await HandleAsync(new MyOrdersRequest
            {
                BuyerId = user.Identity?.Name ?? string.Empty
            }))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(request.BuyerId));
        var payments = await _paymentRepository.ListAsync(
            new OrderPaymentsByOrderIdsSpec(orders.Select(o => o.Id).ToList()));

        var response = new MyOrdersResponse(request.CorrelationId())
        {
            Orders = orders
                .OrderByDescending(o => o.OrderDate)
                .Select(o => OrderDto.FromEntity(o, payments.FirstOrDefault(p => p.OrderId == o.Id)))
                .ToList()
        };

        return Results.Ok(response);
    }
}

public class MyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderDto> Orders { get; set; } = new();
}
