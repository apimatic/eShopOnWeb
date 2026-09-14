using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. The hero flow: ensures a billing customer
/// exists for the eShopOnWeb user (idempotent) and enrolls them, returning the resulting
/// subscription. Safe against double submission — a repeat call returns the existing enrollment.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.Subscriber = user.ToSubscriberIdentity();
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (request.Subscriber is null)
        {
            return Results.Problem("The authenticated token does not identify a user.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem("planHandle is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var result = await subscriptionService.SubscribeAsync(request.Subscriber, request.PlanHandle!, cancellationToken);

            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = result.Subscription.ToDto(),
                AlreadyExisted = result.AlreadyExisted,
            };

            // New enrollment -> 201 Created; idempotent replay of an existing one -> 200 OK.
            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created("/api/my-subscriptions", response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Microsoft.eShopWeb.Infrastructure.Maxio.MaxioApiException ex)
        {
            return Results.Problem(
                $"The billing system rejected the request: {ex.Message}",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
