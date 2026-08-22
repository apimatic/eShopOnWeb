using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, ICheckoutService>
{
    private readonly PayPalSettings _payPalSettings;

    public ListMyOrdersEndpoint(PayPalSettings payPalSettings)
    {
        _payPalSettings = payPalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ICheckoutService checkout, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await checkout.ListMyOrdersAsync(buyerId);
                var response = new ListMyOrdersResponse
                {
                    Orders = orders.Select(o => OrderDtoMapper.From(o, _payPalSettings.Currency)).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ICheckoutService checkout) =>
        throw new System.NotSupportedException("Use the route handler.");
}

public class ListMyOrdersResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}
