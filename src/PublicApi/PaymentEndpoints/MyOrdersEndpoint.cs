using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Lists the caller's orders with their current payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (FinixPaymentService payments, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(payments, user, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        FinixPaymentService payments,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var response = new MyOrdersResponse(System.Guid.NewGuid());
        var buyerId = ClaimsHelper.GetBuyerId(user);
        var orders = await payments.GetOrdersForBuyerAsync(buyerId, ct);
        response.Orders = orders.Select(o => o.ToSummary()).ToList();
        return Results.Ok(response);
    }
}
