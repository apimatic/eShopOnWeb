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
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IReadRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPayPalGateway _payPal;

    public GetMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor, IPayPalGateway payPal)
    {
        _httpContextAccessor = httpContextAccessor;
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReadRepository<Order> orders) =>
            {
                return await HandleAsync(new GetMyOrdersRequest(), orders);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, IReadRepository<Order> orders)
    {
        var buyerId = _httpContextAccessor.HttpContext!.RequireUserName();
        var list = await orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var response = new MyOrdersResponse
        {
            Orders = list.Select(o => OrderResponseMapper.From(o, _payPal.Currency)).ToList()
        };
        return Results.Ok(response);
    }
}

public class GetMyOrdersRequest
{
}

public class MyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}
