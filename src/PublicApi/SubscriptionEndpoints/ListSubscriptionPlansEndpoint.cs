using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioSubscriptionService service, HttpContext context) =>
            {
                return await HandleAsync(service);
            })
           .Produces<ListSubscriptionPlansResponse>(StatusCodes.Status200OK)
           .WithName("ListSubscriptionPlans")
           .RequireAuthorization()
           .WithTags("SubscriptionEndpoints")
           .WithSummary("List available subscription plans");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService service)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await service.GetAvailablePlansAsync();
            response.Plans.AddRange(plans);
            return Results.Ok(response);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
