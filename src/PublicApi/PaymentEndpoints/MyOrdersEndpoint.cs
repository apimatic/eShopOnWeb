using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class MyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>GET /api/my-orders — the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IReadRepository<Order>, CancellationToken>
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
            (IReadRepository<Order> orderRepository, CancellationToken cancellationToken) =>
                await HandleAsync(orderRepository, cancellationToken))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(IReadRepository<Order> orderRepository, CancellationToken cancellationToken)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var orders = await orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);

        return Results.Ok(new MyOrdersResponse
        {
            Orders = orders.Select(PaymentMapping.ToSummary).ToList()
        });
    }
}
