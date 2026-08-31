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
public class ListMyOrdersEndpoint : IEndpoint<IResult, IRepository<Order>, IRepository<Payment>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<Order> orderRepository, IRepository<Payment> paymentRepository) =>
            {
                return await HandleAsync(orderRepository, paymentRepository);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<Order> orderRepository, IRepository<Payment> paymentRepository)
    {
        var response = new ListMyOrdersResponse();
        var buyerId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var payments = await paymentRepository.ListAsync(new PaymentsByBuyerSpec(buyerId));
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        response.Orders = orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => OrderDto.FromOrder(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();

        return Results.Ok(response);
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new List<OrderDto>();
}
