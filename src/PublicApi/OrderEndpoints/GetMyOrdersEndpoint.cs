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

public class GetMyOrdersEndpoint : IEndpoint<IResult, string, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(PaymentHttp.BuyerId(httpContext), paymentService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IOrderPaymentService paymentService)
    {
        try
        {
            var orders = await paymentService.GetMyOrdersAsync(buyerId);
            return Results.Ok(new MyOrdersResponse
            {
                Orders = orders.Select(OrderResponse.From).ToList()
            });
        }
        catch (System.Exception ex)
        {
            return PaymentHttp.FromException(ex);
        }
    }
}

public class MyOrdersResponse
{
    public System.Collections.Generic.List<OrderResponse> Orders { get; set; } = new();
}
