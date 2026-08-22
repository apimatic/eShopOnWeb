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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersEndpoint : IEndpoint<IResult, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService payments, HttpContext http) =>
            {
                var orders = await payments.ListMyOrdersAsync(http.User.RequireBuyerId(), default);
                return Results.Ok(new MyOrdersResponse
                {
                    Orders = orders.Select(OrderResponse.From).ToList()
                });
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderPaymentService payments) =>
        Task.FromResult(Results.Ok());
}

public class MyOrdersResponse : BaseResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}
