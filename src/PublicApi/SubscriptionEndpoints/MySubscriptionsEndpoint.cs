using System.Linq;
using System.Security.Claims;
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

/// <summary>Lists the authenticated user's own subscriptions. Mirrors CatalogItemListPagedEndpoint.</summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/mine",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                var request = new MySubscriptionsRequest { UserId = user.Identity?.Name };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrEmpty(request.UserId))
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse(request.CorrelationId());

        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(request.UserId);
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionEndpointMappers.ToDto));
        }
        catch (BillingProviderException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Billing provider error");
        }

        return Results.Ok(response);
    }
}
