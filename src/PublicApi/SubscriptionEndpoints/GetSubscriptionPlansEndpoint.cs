using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService)
    {
        var response = new GetSubscriptionPlansResponse();

        try
        {
            var plans = await subscriptionService.GetSubscriptionPlansAsync();

            foreach (var plan in plans)
            {
                response.Plans.Add(new SubscriptionPlanDto
                {
                    Handle = plan.Handle,
                    Name = plan.Name,
                    PricePerMonth = plan.PricePerMonth,
                    Description = plan.Description
                });
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
