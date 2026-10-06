using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a Maxio plan (idempotent)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, user, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(400)
            .Produces(401)
            .Produces(404)
            .Produces(502)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return Results.Problem(
                detail: "A plan handle is required to subscribe.",
                title: "Invalid subscription request.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var email = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.Unauthorized();
        }

        var shopper = new ShopperIdentity(email);
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var outcome = await subscriptionService.SubscribeAsync(shopper, request.PlanHandle);
            response.Subscription = outcome.Subscription.ToSubscriptionDto();
            response.AlreadySubscribed = outcome.AlreadySubscribed;
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return SubscriptionEndpointErrors.PlanNotFound(ex);
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionEndpointErrors.Upstream(ex);
        }
        catch (MaxioConfigurationException ex)
        {
            return SubscriptionEndpointErrors.NotConfigured(ex);
        }

        return response.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/subscriptions/{response.Subscription.Id}", response);
    }
}
