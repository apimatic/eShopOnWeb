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
/// Lists the subscription plans available to shoppers (the products in the configured Maxio product
/// family). JWT-authenticated.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioSubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken) =>
                await HandleAsync(subscriptionService, cancellationToken))
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "List subscription plans",
                description: "Lists the recurring subscription plans available to enroll in."));
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();
        var plans = await subscriptionService.GetPlansAsync(cancellationToken);
        response.Plans = plans.Select(p => p.ToDto()).ToList();
        return Results.Ok(response);
    }
}
