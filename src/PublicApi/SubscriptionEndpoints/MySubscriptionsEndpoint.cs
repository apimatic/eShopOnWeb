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

/// <summary>
/// Returns the calling user's subscription, or null if they have none (UC1 success state).
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/mine",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var request = new MySubscriptionsRequest { UserId = user.Identity!.Name! };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<MySubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var subscription = await subscriptionService.GetMySubscriptionAsync(request.UserId);
        var response = new MySubscriptionResponse
        {
            Subscription = subscription == null ? null : SubscriptionDto.FromSubscription(subscription)
        };
        return Results.Ok(response);
    }
}

public class MySubscriptionsRequest : BaseRequest
{
    public string UserId { get; set; } = string.Empty;
}

public class MySubscriptionResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; set; }
}
