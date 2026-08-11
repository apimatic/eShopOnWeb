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

/// <summary>Returns the caller's own orders together with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IPaymentService _payments;

    public MyOrdersEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) => await HandleAsync(user))
            .Produces<MyOrdersResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        var views = await _payments.GetMyOrdersAsync(buyerId);
        var orders = views.Select(v => v.Order.ToDto(v.Payment)).ToList();
        return Results.Ok(new MyOrdersResponse(orders));
    }
}
