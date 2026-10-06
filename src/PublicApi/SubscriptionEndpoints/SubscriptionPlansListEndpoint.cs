using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the subscription plans available in the configured Maxio product family
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<SubscriptionPlansListResponse>()
            .Produces(401)
            .Produces(502)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService)
    {
        var response = new SubscriptionPlansListResponse();

        try
        {
            var plans = await subscriptionService.GetAvailablePlansAsync();
            response.Plans.AddRange(plans.Select(plan => plan.ToPlanDto()));
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionEndpointErrors.Upstream(ex);
        }
        catch (MaxioConfigurationException ex)
        {
            return SubscriptionEndpointErrors.NotConfigured(ex);
        }

        return Results.Ok(response);
    }
}
