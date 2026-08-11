using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>GET /api/my-orders — the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service, HttpContext http) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = http.BuyerId() }, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await service.GetMyOrdersAsync(request.BuyerId);
        var response = new MyOrdersResponse
        {
            Orders = orders.Select(OrderResponse.From).OrderByDescending(o => o.OrderId).ToList()
        };
        return Results.Ok(response);
    }
}

public class MyOrdersRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class MyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}
