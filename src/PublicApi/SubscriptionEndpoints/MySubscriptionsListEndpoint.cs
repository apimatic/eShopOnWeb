using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions, as reported by Maxio (the system of record).
/// The user is identified solely by their token, so the list is always their own.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, SubscriberIdentity, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(
                Summary = "Lists the current user's subscriptions",
                Description = "Lists subscriptions belonging to the authenticated shopper",
                OperationId = "subscriptions.listMine",
                Tags = new[] { "SubscriptionEndpoints" })]
            async (ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                var email = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrWhiteSpace(email))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(SubscriberIdentity.FromEmail(email), subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriberIdentity subscriber, IMaxioSubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse();
        var subscriptions = await subscriptionService.GetSubscriptionsAsync(subscriber);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDtoMappings.ToDto));
        return Results.Ok(response);
    }
}
