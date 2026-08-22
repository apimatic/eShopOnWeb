using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderCheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PayPalSettings _payPalSettings;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor, PayPalSettings payPalSettings)
    {
        _httpContextAccessor = httpContextAccessor;
        _payPalSettings = payPalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(checkout);
            })
            .Produces<ListOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderCheckoutService checkout)
    {
        var buyerId = BuyerIdentity.RequireBuyerId(_httpContextAccessor.HttpContext!.User);
        var orders = await checkout.ListMyOrdersAsync(buyerId);
        return Results.Ok(new ListOrdersResponse
        {
            Orders = orders.Select(o => OrderResponse.From(o, _payPalSettings.Currency)).ToList()
        });
    }
}
