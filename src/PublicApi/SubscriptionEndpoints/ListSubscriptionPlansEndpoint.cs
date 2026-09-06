using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints")
           .WithName("ListSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        try
        {
            var plans = await subscriptionService.GetSubscriptionPlansAsync(CancellationToken.None);
            var response = new ListSubscriptionPlansResponse { Plans = plans };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to fetch subscription plans: {ex.Message}", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public sealed class ListSubscriptionPlansResponse
{
    public SubscriptionPlanDto[] Plans { get; set; } = Array.Empty<SubscriptionPlanDto>();
}
