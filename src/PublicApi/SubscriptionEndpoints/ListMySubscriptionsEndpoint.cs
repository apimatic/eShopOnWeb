using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the calling user's subscriptions, read live from the billing system of record
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billingService, HttpContext httpContext) =>
            {
                var user = await BillingUserResolver.ResolveAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(user, billingService, httpContext.RequestAborted);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    /// <summary>The caller's identity is required; this overload exists for the endpoint contract only.</summary>
    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService) =>
        Task.FromResult(Results.Unauthorized());

    public async Task<IResult> HandleAsync(BillingUser user, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await billingService.GetSubscriptionsAsync(user, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));

        return Results.Ok(response);
    }
}
