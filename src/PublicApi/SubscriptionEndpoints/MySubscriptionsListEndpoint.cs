using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions (GET /api/my-subscriptions).
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var request = new MySubscriptionsRequest { UserName = GetUserName(user) };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await subscriptionService.GetSubscriptionsAsync(
            request.UserName, CancellationToken.None);

        response.Subscriptions = new List<SubscriptionDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(CreateSubscriptionEndpoint.Map(subscription));
        }

        return Results.Ok(response);
    }

    private static string GetUserName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidSubscriptionRequestException(
                "The bearer token did not carry a user identity.");
        }

        return name;
    }
}
