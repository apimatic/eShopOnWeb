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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// The caller's own orders with their payment state. GET /api/my-orders
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _service;

    public MyOrdersEndpoint(IOrderPaymentService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) => await HandleAsync(user))
            .Produces<MyOrderDto[]>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var orders = await _service.GetMyOrdersAsync(buyerId);
        return Results.Ok(orders.Select(o => o.ToDto()).ToList());
    }
}
