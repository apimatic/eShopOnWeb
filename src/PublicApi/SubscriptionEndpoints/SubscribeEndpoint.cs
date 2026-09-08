using System;
using System.Net.Http;
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
/// Subscribes the signed-in shopper to a plan (POST /api/subscriptions). Idempotent: subscribing to
/// a plan the user already has a live subscription to returns that subscription instead of creating
/// a second one.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, System.Security.Claims.ClaimsPrincipal, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, System.Security.Claims.ClaimsPrincipal user, SubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, user, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status504GatewayTimeout)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, System.Security.Claims.ClaimsPrincipal user, SubscriptionService subscriptionService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return SubscriptionEndpointErrors.BadRequest("A productHandle is required.");
        }

        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return SubscriptionEndpointErrors.Unauthorized();
        }

        try
        {
            var result = await subscriptionService.SubscribeAsync(userName, request.ProductHandle.Trim(), CancellationToken.None);

            var response = new SubscribeResponse(request.CorrelationId())
            {
                Created = result.Created,
                Subscription = SubscriptionDtoMapping.ToDto(result.Subscription)
            };
            return Results.Ok(response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return SubscriptionEndpointErrors.UnknownPlan(ex);
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
