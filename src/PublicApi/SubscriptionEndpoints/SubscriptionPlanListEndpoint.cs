using System.Linq;
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
/// Lists the subscription plans a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public SubscriptionPlanListEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        try
        {
            var plans = await _subscriptionService.ListPlansAsync(httpContext.RequestAborted);
            var response = new ListSubscriptionPlansResponse();
            response.Plans.AddRange(plans.Select(SubscriptionEndpointHelpers.ToPlanDto));
            return Results.Ok(response);
        }
        catch (MaxioException ex)
        {
            return SubscriptionEndpointHelpers.ToErrorResult(ex);
        }
    }
}
