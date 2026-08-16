using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Returns the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, string, IPaymentOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentOrderService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(buyerId, service, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(string buyerId, IPaymentOrderService service) =>
        HandleAsync(buyerId, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(string buyerId, IPaymentOrderService service, CancellationToken ct)
    {
        var orders = await service.GetOrdersForBuyerAsync(buyerId, ct);
        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => o.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}
