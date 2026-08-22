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

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService paymentService, HttpContext httpContext) =>
            {
                return await HandleAsync(new GetMyOrdersRequest(), paymentService, httpContext);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(GetMyOrdersRequest request, IOrderPaymentService paymentService) =>
        HandleAsync(request, paymentService, null!);

    private async Task<IResult> HandleAsync(
        GetMyOrdersRequest request,
        IOrderPaymentService paymentService,
        HttpContext httpContext)
    {
        var buyerId = httpContext.GetRequiredUserName();
        var orders = await paymentService.ListMyOrdersAsync(buyerId);
        return Results.Ok(new MyOrdersResponse
        {
            Orders = orders.Select(o => PaymentMapping.ToOrderResponse(o)).ToList()
        });
    }
}

public class GetMyOrdersRequest
{
}

public class MyOrdersResponse
{
    public System.Collections.Generic.List<OrderResponse> Orders { get; set; } = new();
}
