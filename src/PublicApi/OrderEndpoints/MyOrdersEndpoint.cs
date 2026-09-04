using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentEndpoints.OrderPaymentDto? Payment { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's own orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize] async (IHttpContextAccessor httpContextAccessor, IRepository<Order> orderRepository) =>
                await HandleAsync(httpContextAccessor, orderRepository))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request) => throw new NotSupportedException();

    public async Task<IResult> HandleAsync(IHttpContextAccessor httpContextAccessor, IRepository<Order> orderRepository)
    {
        var buyerId = httpContextAccessor.HttpContext.User.RequireBuyerId();

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithPaymentsSpecification(buyerId));

        var response = new MyOrdersResponse();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var dto = new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total()
            };
            foreach (var item in order.OrderItems)
            {
                dto.Items.Add(item.ToDto());
            }
            dto.Payment = order.Payment?.ToDto();
            response.Orders.Add(dto);
        }
        return Results.Ok(response);
    }
}

public class MyOrdersRequest : BaseRequest
{
}
