using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext,
             UserManager<ApplicationUser> userManager,
             ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
                await HandleAsync(httpContext, userManager, billingService, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var username = httpContext.User.Identity?.Name;
        var user = username is null ? null : await userManager.FindByNameAsync(username);
        if (user is null)
        {
            throw new SubscriptionBillingException(HttpStatusCode.Unauthorized, "Unauthorized", "The authenticated user could not be resolved.");
        }

        var subscriptions = await billingService.GetMySubscriptionsAsync(user.Id, cancellationToken);
        return Results.Ok(new MySubscriptionsResponse(subscriptions));
    }
}
