using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>GET /api/my-orders — the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IReadRepository<Order> _orderReadRepository;
    private readonly PayPalSettings _settings;

    public MyOrdersEndpoint(IReadRepository<Order> orderReadRepository, PayPalSettings settings)
    {
        _orderReadRepository = orderReadRepository;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, CancellationToken ct) => await HandleAsync(http))
            .Produces<List<OrderDto>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var buyerId = http.BuyerId();
        var orders = await _orderReadRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), http.RequestAborted);
        var dtos = orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => OrderDto.From(o, _settings.Currency))
            .ToList();
        return Results.Ok(dtos);
    }
}
