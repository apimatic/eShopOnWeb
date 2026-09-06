using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext context, MaxioSubscriptionService subscriptionService) =>
            {
                var userReference = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                   ?? context.User?.FindFirst("sub")?.Value
                                   ?? context.User?.FindFirst(ClaimTypes.Name)?.Value;

                if (string.IsNullOrEmpty(userReference))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(subscriptionService, userReference);
            })
            .RequireAuthorization()
            .Produces<SubscriptionListResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListUserSubscriptions");
    }

    async Task<IResult> IEndpoint<IResult, MaxioSubscriptionService>.HandleAsync(MaxioSubscriptionService subscriptionService)
    {
        // This interface implementation is provided for framework compatibility.
        // The real implementation is called from AddRoute with the user reference.
        throw new NotImplementedException("Must be called through the route handler that provides user context");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService subscriptionService, string userReference = "")
    {
        var response = new SubscriptionListResponse();

        try
        {
            var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(userReference, CancellationToken.None);
            response.Subscriptions.AddRange(subscriptions);

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
