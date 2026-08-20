using System;
using System.Security.Claims;
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

public sealed class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService service, CancellationToken cancellationToken) =>
                await HandleAsync(principal, userManager, service, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public static async Task<IResult> HandleAsync(ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager, ISubscriptionBillingService service,
        CancellationToken cancellationToken)
    {
        var shopper = await SubscriptionEndpointSupport.GetShopperAsync(principal, userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(new MySubscriptionsResponse
            {
                Subscriptions = await service.GetSubscriptionsAsync(shopper, cancellationToken)
            });
        }
        catch (Exception exception) when (exception is MaxioApiException)
        {
            return SubscriptionEndpointSupport.Error(exception);
        }
    }
}
