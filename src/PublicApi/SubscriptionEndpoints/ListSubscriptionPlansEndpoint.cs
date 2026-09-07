using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService) =>
            {
                var response = new ListSubscriptionPlansResponse(Guid.NewGuid());

                try
                {
                    response.Plans = await subscriptionService.GetPlansAsync();
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error fetching subscription plans: {ex.Message}");
                }
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithName("ListSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() => throw new NotImplementedException();
}
