using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersRequest { }

public class MyOrdersResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>Returns the caller's own orders, each with its payment state. Shopper-scoped.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PayPalSettings _settings;

    public MyOrdersEndpoint(IReadRepository<Order> orderRepository,
        IHttpContextAccessor httpContextAccessor, PayPalSettings settings)
    {
        _orderRepository = orderRepository;
        _httpContextAccessor = httpContextAccessor;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async () => await HandleAsync(new MyOrdersRequest()))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentsSpec(buyerId));
        return Results.Ok(new MyOrdersResponse
        {
            Orders = orders.Select(o => OrderDtoMapper.ToDto(o, _settings.Currency)).ToList()
        });
    }
}
