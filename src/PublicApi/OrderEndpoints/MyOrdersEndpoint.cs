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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The signed-in shopper's own orders with their payment state. GET /api/my-orders
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReadRepository<Order> orderRepository, ClaimsPrincipal user) =>
            {
                return await HandleAsync(user.GetBuyerId(), orderRepository);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IReadRepository<Order> orderRepository) => Results.Empty;

    private static async Task<IResult> HandleAsync(string buyerId, IReadRepository<Order> orderRepository)
    {
        var response = new MyOrdersResponse();
        var orders = await orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId));
        response.Orders = orders.Select(OrderSummaryDto.FromEntity).ToList();
        return Results.Ok(response);
    }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse() { }
    public List<OrderSummaryDto> Orders { get; set; } = new();
}
