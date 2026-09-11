using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioApiClient, MaxioOptions>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioApiClient maxioClient, MaxioOptions options) =>
            {
                return await HandleAsync(maxioClient, options);
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioApiClient maxioClient, MaxioOptions options)
    {
        var response = new SubscriptionPlanListResponse();

        var plans = await maxioClient.GetPlansAsync(options.ProductFamilyHandle);
        response.Plans.AddRange(plans);

        return Results.Ok(response);
    }
}
