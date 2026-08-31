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
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IRepository<Order>, IReadRepository<OrderPayment>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<Order> orderRepository, IReadRepository<OrderPayment> paymentRepository) =>
            {
                return await HandleAsync(new ListMyOrdersRequest { Username = OrderMapping.GetUserName(user) }, orderRepository, paymentRepository);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IRepository<Order> orderRepository, IReadRepository<OrderPayment> paymentRepository)
    {
        if (string.IsNullOrEmpty(request.Username))
        {
            return Results.Unauthorized();
        }

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(request.Username));
        var payments = await paymentRepository.ListAsync(new OrderPaymentsByBuyerIdSpec(request.Username));
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        var response = new ListMyOrdersResponse();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            paymentsByOrder.TryGetValue(order.Id, out var payment);
            response.Orders.Add(OrderMapping.ToDto(order, payment));
        }

        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    public string? Username { get; set; }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}
