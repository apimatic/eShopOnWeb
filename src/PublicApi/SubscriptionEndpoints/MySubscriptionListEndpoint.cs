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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the signed-in shopper's subscriptions
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager, ISubscriptionService subscriptionService,
                HttpContext httpContext) =>
            {
                var subscriber = await SubscriptionEndpointSupport.ResolveSubscriberAsync(user, userManager);
                if (subscriber is null)
                    return SubscriptionEndpointSupport.UnknownUser();

                return await HandleAsync(new ListMySubscriptionsRequest(subscriber), subscriptionService, httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .WithTags(SubscriptionEndpointSupport.Tag);
    }

    public Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, null);

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService,
        HttpContext? httpContext)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());
        try
        {
            var list = await subscriptionService.ListSubscriptionsAsync(request.Subscriber,
                httpContext?.RequestAborted ?? CancellationToken.None);
            response.Subscriptions.AddRange(list.Subscriptions.Select(SubscriptionDto.From));
            response.PendingSignups.AddRange(list.PendingSignups.Select(PendingSignupDto.From));
            response.IsComplete = list.IsComplete;
            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex) when (httpContext is not null)
        {
            return SubscriptionEndpointSupport.FromBillingException(ex, httpContext);
        }
    }
}
