using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's Maxio subscriptions.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public MySubscriptionsEndpoint(MaxioSubscriptionService subscriptionService, CurrentUserAccessor currentUserAccessor)
    {
        _subscriptionService = subscriptionService;
        _currentUserAccessor = currentUserAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var (userId, _) = await _currentUserAccessor.GetCurrentUserAsync();

        var subscriptions = await _subscriptionService.ListSubscriptionsAsync(userId);

        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.FromMaxio));

        return Results.Ok(response);
    }
}
