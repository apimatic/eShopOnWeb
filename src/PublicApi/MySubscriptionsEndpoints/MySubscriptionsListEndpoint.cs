using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.MySubscriptionsEndpoints;

/// <summary>
/// Lists the authenticated user's Maxio subscriptions.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsListEndpoint(ISubscriptionService subscriptionService, IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<MySubscriptionsListResponse>()
            .WithTags("MySubscriptionsEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var response = new MySubscriptionsListResponse();

        var httpContext = _httpContextAccessor.HttpContext;
        var subscriber = SubscriberProfileFactory.Create(httpContext?.User, null, null);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await _subscriptionService.ListSubscriptionsAsync(subscriber.Reference, httpContext!.RequestAborted);
        response.Subscriptions.AddRange(subscriptions);
        return Results.Ok(response);
    }
}
