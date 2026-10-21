using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the subscription plans this shop offers
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(subscriptionService, httpContext);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .WithTags(SubscriptionEndpointSupport.Tag);
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptionService) =>
        HandleAsync(subscriptionService, null);

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService, HttpContext? httpContext)
    {
        var response = new ListSubscriptionPlansResponse();
        try
        {
            var plans = await subscriptionService.ListPlansAsync(httpContext?.RequestAborted ?? CancellationToken.None);
            response.Plans.AddRange(plans.Plans.Select(SubscriptionPlanDto.From));
            response.IsComplete = plans.IsComplete;
            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex) when (httpContext is not null)
        {
            return SubscriptionEndpointSupport.FromBillingException(ex, httpContext);
        }
    }
}
