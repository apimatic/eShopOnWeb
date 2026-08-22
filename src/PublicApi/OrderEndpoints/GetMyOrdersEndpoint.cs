using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, ICheckoutOrderService>
{
    private readonly IOptions<PayPalSettings> _payPalSettings;

    public GetMyOrdersEndpoint(IOptions<PayPalSettings> payPalSettings)
    {
        _payPalSettings = payPalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, ICheckoutOrderService checkout) =>
            {
                return await HandleAsync(new GetMyOrdersRequest { BuyerId = http.RequireBuyerId() }, checkout);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, ICheckoutOrderService checkout)
    {
        var orders = await checkout.GetOrdersForBuyerAsync(request.BuyerId, default);
        var currency = _payPalSettings.Value.Currency;
        return Results.Ok(new GetMyOrdersResponse
        {
            Orders = orders.Select(o => OrderDto.From(o, currency)).ToList()
        });
    }
}
