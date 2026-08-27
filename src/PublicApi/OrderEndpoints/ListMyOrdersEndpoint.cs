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
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IRepository<Order>, IRepository<Payment>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<Order> orderRepository, IRepository<Payment> paymentRepository) =>
            {
                return await HandleAsync(new ListMyOrdersRequest { BuyerId = user.GetBuyerId() }, orderRepository, paymentRepository);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IRepository<Order> orderRepository, IRepository<Payment> paymentRepository)
    {
        var response = new ListMyOrdersResponse(request.CorrelationId());

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(request.BuyerId));
        foreach (var order in orders)
        {
            var payment = await paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id));
            response.Orders.Add(OrderDto.FromDomain(order, payment));
        }

        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(System.Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public List<OrderDto> Orders { get; set; } = new List<OrderDto>();
}
