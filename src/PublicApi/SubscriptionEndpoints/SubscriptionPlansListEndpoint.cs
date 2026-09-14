using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available in the configured Maxio product family.
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, IMaxioSubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var plans = await subscriptionService.ListPlansAsync(cancellationToken);
        var response = new ListSubscriptionPlansResponse
        {
            Plans = SubscriptionDtoMapper.ToPlanDtos(plans)
        };

        return Results.Ok(response);
    }
}
