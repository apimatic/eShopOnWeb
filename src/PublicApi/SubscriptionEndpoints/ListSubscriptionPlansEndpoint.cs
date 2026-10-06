using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the subscription plans offered by the configured Maxio product family
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionService, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptionService) =>
        HandleAsync(subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        var catalog = await subscriptionService.GetPlansAsync(cancellationToken);

        response.Plans.AddRange(catalog.Plans.Select(SubscriptionPlanDto.From));
        response.IsTruncated = catalog.IsTruncated;
        return Results.Ok(response);
    }
}
