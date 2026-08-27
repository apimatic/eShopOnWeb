using System;
using System.Collections.Generic;
using System.Linq;
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
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IRepository<Payment> _paymentRepository;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor, IRepository<Payment> paymentRepository)
    {
        _httpContextAccessor = httpContextAccessor;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), orderRepository);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IRepository<Order> orderRepository)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderRepository.ListAsync(new OrdersByBuyerWithItemsSpec(buyerId));
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpec(buyerId));
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        var response = new ListMyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(o => OrderDto.FromEntity(o, paymentsByOrder.GetValueOrDefault(o.Id))).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public List<OrderDto> Orders { get; set; } = new();
}
