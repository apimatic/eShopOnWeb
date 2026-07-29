using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Returns the calling shopper's subscriptions, as reported by Maxio (the system of record).
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user,
                   UserManager<ApplicationUser> userManager,
                   IMaxioSubscriptionService subscriptionService) =>
            {
                var subscriber = await SubscriberContext.ResolveAsync(user, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new MySubscriptionsRequest { Subscriber = subscriber }, subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioSubscriptionService subscriptionService)
    {
        if (request.Subscriber is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsAsync(request.Subscriber);
            var response = new MySubscriptionsResponse(request.CorrelationId())
            {
                Subscriptions = subscriptions.Select(s => s.ToDto()).ToList(),
            };
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(
                title: "Unable to retrieve your subscriptions from the billing provider.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
