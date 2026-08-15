using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>The caller's own orders, each with its payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IReadRepository<Order>, IPayPalPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReadRepository<Order> orderRepository, IPayPalPaymentService payPal) =>
                await HandleAsync(orderRepository, payPal))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IReadRepository<Order> orderRepository, IPayPalPaymentService payPal)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();
        var orders = await orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId));

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => OrderDtoMapper.ToDto(o, payPal.Currency)).ToList()
        };
        return Results.Ok(response);
    }
}
