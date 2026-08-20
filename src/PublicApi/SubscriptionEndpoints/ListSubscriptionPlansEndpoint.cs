using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class ListSubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                (ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
                    HandleAsync(subscriptionService, cancellationToken))
            .Produces<SubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var plans = await subscriptionService.ListPlansAsync(cancellationToken);
        return Results.Ok(new SubscriptionPlansResponse
        {
            SubscriptionPlans = plans.Select(SubscriptionPlanDto.From).ToArray()
        });
    }
}
