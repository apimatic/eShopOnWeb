using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, string, IOrderPaymentService>
{
    private readonly IPayPalGateway _payPal;

    public ListMyOrdersEndpoint(IPayPalGateway payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IOrderPaymentService payments) =>
                await HandleAsync(CallerIdentity.GetBuyerId(user), payments))
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IOrderPaymentService payments)
    {
        var orders = await payments.ListBuyerOrdersAsync(buyerId);
        var currency = _payPal.Currency;
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = orders.Select(o => OrderDto.From(o, currency)).ToList()
        });
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}
