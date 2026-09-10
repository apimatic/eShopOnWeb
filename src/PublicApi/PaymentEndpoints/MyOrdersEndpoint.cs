using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Lists the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service, ClaimsPrincipal user) =>
                await HandleAsync(service, user))
            .Produces<MyOrdersResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderPaymentService service, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.Require(user);
        var views = await service.GetMyOrdersAsync(buyerId, CancellationToken.None);
        return Results.Ok(new MyOrdersResponse { Orders = views.Select(OrderPaymentResponse.From).ToList() });
    }
}
