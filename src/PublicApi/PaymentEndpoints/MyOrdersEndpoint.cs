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
/// GET /api/my-orders — the caller's own orders with their payment state. Shopper-scoped: only the
/// caller's orders are ever returned.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, string, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IOrderPaymentService service) =>
                await HandleAsync(user.GetBuyerId(), service))
            .Produces<MyOrdersResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(string buyerId, IOrderPaymentService service)
    {
        var orders = await service.GetOrdersForBuyerAsync(buyerId);
        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => o.ToResponse()).ToList()
        };
        return Results.Ok(response);
    }
}
