using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Returns the caller's own subscription, or null if they have none.</summary>
public class MySubscriptionEndpoint : IEndpoint<IResult, MySubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/mine",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new MySubscriptionRequest(user.Identity!.Name!), subscriptionService);
            })
            .Produces<MySubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new MySubscriptionResponse(request.CorrelationId());

        var subscription = await subscriptionService.GetMySubscriptionAsync(request.BuyerId);
        response.Subscription = subscription is null ? null : SubscriptionDto.FromDomain(subscription);

        return Results.Ok(response);
    }
}
