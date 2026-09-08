using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan in Maxio (idempotent).
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, MaxioSubscriptionService subscriptionService, ClaimsPrincipal user, UserManager<ApplicationUser> userManager) =>
            {
                request.Subscriber = await SubscriptionEndpointSupport.ResolveSubscriberAsync(user, userManager);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, MaxioSubscriptionService subscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        if (request.Subscriber is null)
        {
            return SubscriptionEndpointSupport.BadRequest("The authenticated user could not be resolved.");
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return SubscriptionEndpointSupport.BadRequest("A plan handle is required.");
        }

        SubscribeResult result;
        try
        {
            result = await subscriptionService.SubscribeAsync(request.Subscriber, request.PlanHandle.Trim(), request.IdempotencyKey);
        }
        catch (PlanNotOfferedException ex)
        {
            return SubscriptionEndpointSupport.BadRequest(ex.Message);
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionEndpointSupport.MapMaxioFailure(ex);
        }
        catch (MaxioIntegrationException ex)
        {
            return SubscriptionEndpointSupport.BadRequest(ex.Message);
        }

        response.Subscription = SubscriptionEndpointSupport.ToDto(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("/api/my-subscriptions", response);
    }
}
