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

public class GetMyOrdersEndpoint : IEndpoint<IResult, string, IPaymentCheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPaymentCheckoutService payments) =>
            {
                return await HandleAsync(EndpointUser.BuyerId(_httpContextAccessor.HttpContext!), payments);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IPaymentCheckoutService payments)
    {
        var orders = await payments.GetMyOrdersAsync(buyerId);
        return Results.Ok(new MyOrdersResponse
        {
            Orders = orders.Select(o => OrderResponseMapper.Map(o, payments.Currency)).ToList()
        });
    }
}
