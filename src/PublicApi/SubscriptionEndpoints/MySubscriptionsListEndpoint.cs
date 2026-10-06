using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions the authenticated user has with Maxio Advanced Billing.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, SubscriptionService, ICurrentUser>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionService subscriptionService, ICurrentUser currentUser) =>
            {
                return await HandleAsync(subscriptionService, currentUser);
            })
            .Produces<MySubscriptionsListResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionService subscriptionService, ICurrentUser currentUser)
    {
        var response = new MySubscriptionsListResponse();

        var userReference = currentUser.GetUserReference();
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetCustomerSubscriptionsAsync(userReference);

        response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.MapSubscription));

        return Results.Ok(response);
    }
}
