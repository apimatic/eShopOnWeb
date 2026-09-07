using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionPlansEndpoint
{
    public static async Task<IResult> HandleAsync(MaxioService maxioService)
    {
        var response = new SubscriptionPlansResponse();

        try
        {
            var plans = await maxioService.GetSubscriptionPlansAsync();
            response.Plans.AddRange(plans);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class SubscriptionPlansResponse
{
    public List<SubscriptionPlan> Plans { get; } = new();
}
