using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator action. Cancels before fulfilment, voiding any
/// authorization so the shopper's held funds are released (no money ever moved). Idempotent.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IOrderPaymentService _orders;
    private readonly PayPalSettings _settings;

    public CancelOrderEndpoint(IOrderPaymentService orders, PayPalSettings settings)
    {
        _orders = orders;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, CancellationToken ct) => await HandleAsync(http))
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var order = await _orders.CancelAsync(http.RouteInt("orderId"), http.RequestAborted);
        return Results.Ok(OrderDto.From(order, _settings.Currency));
    }
}
