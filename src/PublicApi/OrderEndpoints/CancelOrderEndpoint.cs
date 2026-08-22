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

public class CancelOrderEndpoint : IEndpoint<IResult, int, ICheckoutService>
{
    private readonly PayPalSettings _payPalSettings;

    public CancelOrderEndpoint(PayPalSettings payPalSettings)
    {
        _payPalSettings = payPalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ICheckoutService checkout) =>
            {
                var order = await checkout.CancelAsync(orderId);
                return Results.Ok(OrderDtoMapper.From(order, _payPalSettings.Currency));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, ICheckoutService checkout) =>
        throw new System.NotSupportedException("Use the route handler.");
}
