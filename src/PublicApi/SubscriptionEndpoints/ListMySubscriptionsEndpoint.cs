using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions. GET /api/my-subscriptions — JWT authenticated.
/// Returns an empty list when the shopper has no Maxio customer yet.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, CancellationToken>
{
    private readonly IMaxioBillingService _billing;
    private readonly ICurrentShopperService _currentShopper;

    public ListMySubscriptionsEndpoint(IMaxioBillingService billing, ICurrentShopperService currentShopper)
    {
        _billing = billing;
        _currentShopper = currentShopper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CancellationToken ct) => await HandleAsync(ct))
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancellationToken ct)
    {
        try
        {
            var shopper = await _currentShopper.GetCurrentShopperAsync(ct);
            var subscriptions = await _billing.ListMySubscriptionsAsync(shopper, ct);
            return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = subscriptions.ToList() });
        }
        catch (MaxioBillingException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: (int)ex.StatusCode);
        }
    }
}
