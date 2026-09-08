using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions of the authenticated shopper, as recorded by Maxio.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (MaxioSubscriptionService subscriptionService, ClaimsPrincipal user, UserManager<ApplicationUser> userManager) =>
            {
                var request = new ListMySubscriptionsRequest
                {
                    Subscriber = await SubscriptionEndpointSupport.ResolveSubscriberAsync(user, userManager),
                };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, MaxioSubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        if (request.Subscriber is null)
        {
            return SubscriptionEndpointSupport.BadRequest("The authenticated user could not be resolved.");
        }

        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsByCustomerReferenceAsync(request.Subscriber.Reference);
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionEndpointSupport.ToDto));
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionEndpointSupport.MapMaxioFailure(ex);
        }

        return Results.Ok(response);
    }
}
