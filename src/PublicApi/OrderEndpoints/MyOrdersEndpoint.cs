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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>The caller's own orders together with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(user, service, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderPaymentService service)
        => HandleAsync(user, service, default);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct)
    {
        var buyerId = user.BuyerId();
        var orders = await service.GetMyOrdersAsync(buyerId, ct);

        var response = new MyOrdersResponse();
        foreach (var pair in orders)
        {
            response.Orders.Add(OrderSummaryDto.From(pair.Order, pair.Payment));
        }
        return Results.Ok(response);
    }
}
