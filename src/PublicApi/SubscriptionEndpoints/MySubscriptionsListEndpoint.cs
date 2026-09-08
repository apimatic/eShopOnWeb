using System.Linq;
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
/// Lists the authenticated user's subscriptions, read live from Maxio Advanced Billing.
/// GET /api/my-subscriptions
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, string>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public MySubscriptionsListEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (ClaimsPrincipal user) =>
            {
                var reference = SubscriberIdentity.RequireUserReference(user);
                return await HandleAsync(reference);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(string userReference)
    {
        var response = new MySubscriptionsResponse();

        var subscriptions = await _subscriptionService.GetSubscriptionsForCustomerAsync(userReference);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.From));

        return Results.Ok(response);
    }
}
