using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services.Subscriptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(user, subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionService subscriptionService)
    {
        try
        {
            var response = new ListSubscriptionPlansResponse();

            var plans = await subscriptionService.ListAvailablePlansAsync(default);

            response.Plans.AddRange(plans.Select(plan => new SubscriptionPlanDto
            {
                Id = plan.Id,
                Handle = plan.Handle,
                Name = plan.Name,
                Description = plan.Description,
                PriceInCents = plan.PriceInCents,
                Price = plan.Price,
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit
            }));

            return Results.Ok(response);
        }
        catch (System.Exception ex)
        {
            return SubscriptionEndpointErrorMapping.ToErrorResult(ex);
        }
    }
}
