using System.Linq;
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
/// Lists the calling customer's own subscriptions (UC1, success state).
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService) => await HandleAsync(subscriptionService))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var userReference = _httpContextAccessor.CurrentUserReference();
        if (userReference is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.ListMySubscriptionsAsync(userReference);

        var response = new MySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(subscription => subscription.ToDto()).ToList()
        };

        return Results.Ok(response);
    }
}
