using System;
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
/// Lists the subscription plans on offer (the products of the configured Maxio product family).
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, MaxioSubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioSubscriptionService subscriptions, HttpContext httpContext) =>
            {
                return await HandleAsync(subscriptions, httpContext);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService subscriptions, HttpContext httpContext)
    {
        var correlationId = Guid.NewGuid();
        try
        {
            var catalog = await subscriptions.ListPlansAsync(correlationId, httpContext.RequestAborted);
            var response = new ListSubscriptionPlansResponse(correlationId)
            {
                Plans = catalog.Plans.Select(SubscriptionEndpointMapping.ToDto).ToList(),
                Truncated = catalog.Truncated,
            };
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointMapping.Failure(ex);
        }
    }
}
