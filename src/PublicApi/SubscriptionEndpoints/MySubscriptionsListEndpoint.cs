using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated user's subscriptions, as recorded by the billing provider
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, Subscriber, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberResolver.ResolveAsync(user, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(subscriber, billing, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces<ErrorDetails>(StatusCodes.Status502BadGateway)
            .Produces<ErrorDetails>(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(Subscriber subscriber, ISubscriptionBillingService billing) =>
        HandleAsync(subscriber, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(Subscriber subscriber, ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var result = await billing.GetSubscriptionsAsync(subscriber, cancellationToken);

        response.Subscriptions.AddRange(result.Subscriptions.Select(SubscriptionDto.From));
        response.HasUnconfirmedEnrollment = result.HasUnconfirmedEnrollment;

        return Results.Ok(response);
    }
}
