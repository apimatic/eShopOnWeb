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
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<Payment> _paymentRepository;

    public MyOrdersEndpoint(IReadRepository<Order> orderRepository, IReadRepository<Payment> paymentRepository)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerIdSpecification(buyerId));
        var latestPaymentByOrder = payments
            .GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First());

        var response = new MyOrdersResponse
        {
            Orders = orders
                .OrderByDescending(o => o.OrderDate)
                .Select(o => OrderDto.FromEntity(o, latestPaymentByOrder.GetValueOrDefault(o.Id)))
                .ToList()
        };
        return Results.Ok(response);
    }
}
