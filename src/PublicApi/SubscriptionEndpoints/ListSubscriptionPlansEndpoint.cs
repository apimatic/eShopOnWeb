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
/// List the subscription plans offered by the configured Maxio product family
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionService service, CancellationToken ct) =>
            {
                return await HandleAsync(service, ct);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionService service, CancellationToken ct)
    {
        var response = new ListSubscriptionPlansResponse();
        try
        {
            var plans = await service.ListPlansAsync(ct);
            response.Plans.AddRange(plans.Plans);
            response.IsPartial = plans.IsPartial;
            return Results.Ok(response);
        }
        catch (SubscriptionException ex)
        {
            return SubscriptionEndpointHelpers.Failure(ex);
        }
    }
}
