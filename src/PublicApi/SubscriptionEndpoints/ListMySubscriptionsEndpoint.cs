using System.Linq;
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
/// Lists the subscriptions of the authenticated user, with plan, price,
/// state and next-billing-date.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionUserResolver, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionUserResolver userResolver, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(userResolver, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionUserResolver userResolver, ISubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse();

        var user = await userResolver.ResolveAsync();
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await subscriptionService.ListUserSubscriptionsAsync(user);
            response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.ToDto));
            return Results.Ok(response);
        }
        catch (BillingProviderException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Billing provider error",
                detail: ex.Message);
        }
    }
}