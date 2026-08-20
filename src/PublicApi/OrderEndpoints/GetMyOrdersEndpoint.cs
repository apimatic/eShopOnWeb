using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersRequest : BaseRequest
{
    internal string BuyerId { get; set; } = string.Empty;
}

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, ICheckoutService>
{
    private readonly IPaymentSettings _paymentSettings;

    public GetMyOrdersEndpoint(IPaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, ICheckoutService checkout) =>
            {
                return await HandleAsync(new GetMyOrdersRequest { BuyerId = CreateOrderEndpoint.RequireBuyerId(http) }, checkout);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, ICheckoutService checkout)
    {
        var orders = await checkout.GetMyOrdersAsync(request.BuyerId);
        return Results.Ok(new MyOrdersResponse
        {
            Orders = orders.Select(o => OrderResponse.From(o, _paymentSettings.Currency)).ToList()
        });
    }
}
