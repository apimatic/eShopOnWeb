using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionServices;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the signed-in shopper's subscriptions (GET /api/my-subscriptions).
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, SubscriptionService subscriptionService) =>
            {
                return await HandleAsync(user, subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status504GatewayTimeout)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, SubscriptionService subscriptionService)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return SubscriptionEndpointErrors.Unauthorized();
        }

        try
        {
            var subscriptions = await subscriptionService.GetMySubscriptionsAsync(userName, CancellationToken.None);

            var response = new MySubscriptionsResponse();
            foreach (var subscription in subscriptions)
            {
                response.Subscriptions.Add(SubscriptionDtoMapping.ToDto(subscription));
            }

            return Results.Ok(response);
        }
        catch (MaxioConfigurationException ex)
        {
            return SubscriptionEndpointErrors.NotConfigured(ex);
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionEndpointErrors.MaxioFailure(ex);
        }
        catch (HttpRequestException ex)
        {
            return SubscriptionEndpointErrors.MaxioUnreachable(ex);
        }
        catch (OperationCanceledException)
        {
            return SubscriptionEndpointErrors.MaxioTimeout();
        }
    }
}
