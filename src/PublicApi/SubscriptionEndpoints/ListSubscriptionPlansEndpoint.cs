using System.Linq;
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
/// Lists the subscription plans available in the configured Maxio product family.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioBillingService billingService) => await HandleAsync(billingService))
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Lists available subscription plans",
                "Returns the plans in the configured Maxio product family. Requires a bearer token."));
    }

    public Task<IResult> HandleAsync(IMaxioBillingService billingService) =>
        SubscriptionResults.RunAsync(async () =>
        {
            var response = new ListSubscriptionPlansResponse();
            var plans = await billingService.ListPlansAsync();
            response.Plans.AddRange(plans.Select(SubscriptionPlanDto.FromDomain));
            return Results.Ok(response);
        });
}
