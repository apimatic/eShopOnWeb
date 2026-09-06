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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the subscription plans a shopper can subscribe to
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionService>
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
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Lists the available subscription plans",
                description: "Lists the plans offered by the configured billing product family. Subscribe with a plan's handle."));
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptionService) =>
        HandleAsync(subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await subscriptionService.GetAvailablePlansAsync(cancellationToken);
        response.SubscriptionPlans.AddRange(plans.Select(p => p.ToDto()));

        return Results.Ok(response);
    }
}
