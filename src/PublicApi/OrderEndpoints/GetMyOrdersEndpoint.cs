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
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPayPalPaymentsClient _payPal;

    public GetMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor, IPayPalPaymentsClient payPal)
    {
        _httpContextAccessor = httpContextAccessor;
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderPaymentService orders) => await HandleAsync(orders))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderPaymentService orders)
    {
        var buyerId = CallerIdentity.GetBuyerId(_httpContextAccessor.HttpContext);
        var list = await orders.ListMyOrdersAsync(buyerId);
        return Results.Ok(new MyOrdersResponse
        {
            Orders = list.Select(o => OrderResponse.From(o, _payPal.Currency)).ToList()
        });
    }
}

public class MyOrdersResponse
{
    public System.Collections.Generic.List<OrderResponse> Orders { get; set; } = new();
}
