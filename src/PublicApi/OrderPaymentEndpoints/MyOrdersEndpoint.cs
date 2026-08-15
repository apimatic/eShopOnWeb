using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// GET /api/my-orders — the caller's own orders with their payment state. Shopper-scoped.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public MyOrdersEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service) =>
                await HandleAsync(service))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderPaymentService service)
    {
        var buyerId = CurrentUser.RequireBuyerId(_http);
        var orders = await service.GetMyOrdersAsync(buyerId, CurrentUser.RequestAborted(_http));
        var response = new MyOrdersResponse
        {
            Orders = orders.Select(OrderPaymentResponse.From).ToList()
        };
        return Results.Ok(response);
    }
}
