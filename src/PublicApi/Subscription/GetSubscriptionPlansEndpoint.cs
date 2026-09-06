using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.eShopWeb.PublicApi.Subscription;

public static class GetSubscriptionPlansEndpoint
{
    public static void MapGetSubscriptionPlans(this WebApplication app)
    {
        app.MapGet("api/subscription-plans",
            GetPlans)
            .WithName("GetSubscriptionPlans")
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> GetPlans(MaxioSubscriptionService subscriptionService)
    {
        try
        {
            var plans = await subscriptionService.GetSubscriptionPlansAsync();
            var response = new GetSubscriptionPlansResponse
            {
                Plans = plans.ToList()
            };
            return Results.Ok(response);
        }
        catch (MaxioServiceException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}

public class GetSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class EmptyRequest { }
