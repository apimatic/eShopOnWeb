using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans offered (the products of the configured Maxio product family)
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionBillingService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billing, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        var catalog = await billing.GetPlansAsync(cancellationToken);
        response.Plans.AddRange(catalog.Plans.Select(SubscriptionPlanDto.From));
        response.Complete = catalog.IsComplete;

        return Results.Ok(response);
    }
}
