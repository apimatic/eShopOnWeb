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
/// Lists the subscription plans available in the configured Maxio product family.
/// GET /api/subscription-plans — JWT authenticated (any signed-in shopper).
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, CancellationToken>
{
    private readonly IMaxioBillingService _billing;

    public ListSubscriptionPlansEndpoint(IMaxioBillingService billing)
    {
        _billing = billing;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CancellationToken ct) => await HandleAsync(ct))
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancellationToken ct)
    {
        try
        {
            var plans = await _billing.ListPlansAsync(ct);
            return Results.Ok(new ListSubscriptionPlansResponse { Plans = plans.ToList() });
        }
        catch (MaxioBillingException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: (int)ex.StatusCode);
        }
    }
}
