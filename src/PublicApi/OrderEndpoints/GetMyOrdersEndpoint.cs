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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPaymentCurrencyAccessor _currency;

    public GetMyOrdersEndpoint(IReadRepository<Order> orderRepository, IPaymentCurrencyAccessor currency)
    {
        _orderRepository = orderRepository;
        _currency = currency;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) => await HandleAsync(user))
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.RequireUserName();
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        return Results.Ok(new GetMyOrdersResponse
        {
            Orders = orders
                .OrderByDescending(o => o.OrderDate)
                .Select(o => OrderDtoMapper.FromOrder(o, _currency.Currency))
                .ToList()
        });
    }
}

public class GetMyOrdersResponse
{
    public System.Collections.Generic.List<OrderDto> Orders { get; set; } = new();
}
