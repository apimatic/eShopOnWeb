using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the calling shopper's subscriptions (read from the billing provider)
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, HttpContext httpContext) =>
            {
                return await HandleAsync(billingService, httpContext.User, httpContext.RequestAborted);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status502BadGateway)
            .WithTags(SubscriptionEndpointMappings.TAG);
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService) =>
        HandleAsync(billingService, new ClaimsPrincipal(), CancellationToken.None);

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var userName = SubscriptionEndpointMappings.UserName(user);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        try
        {
            var mine = await billingService.GetMySubscriptionsAsync(userName, cancellationToken);
            response.Subscriptions.AddRange(mine.Subscriptions.Select(SubscriptionEndpointMappings.ToDto));
            response.EnrollmentStatus = mine.EnrollmentStatus;
            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionEndpointMappings.ToResult(ex, response.CorrelationId());
        }
    }
}
