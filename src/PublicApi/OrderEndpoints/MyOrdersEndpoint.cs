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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record MyOrdersResponse(IReadOnlyList<OrderPaymentDto> Orders);

/// <summary>
/// GET /api/my-orders — the caller's own orders with their payment state. Shopper-scoped: only the
/// caller's orders are returned.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IReadRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPaymentSettings _settings;

    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor, IPaymentSettings settings)
    {
        _httpContextAccessor = httpContextAccessor;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReadRepository<Order> orderRepository) =>
                await HandleAsync(orderRepository))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IReadRepository<Order> orderRepository)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();
        var orders = await orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId));

        var dtos = orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => PaymentDtoMapper.ToDto(o, _settings.Currency))
            .ToList();

        return Results.Ok(new MyOrdersResponse(dtos));
    }
}
