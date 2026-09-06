using System.Collections.Generic;
using System.Globalization;
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
/// Subscribe the signed-in shopper to a plan.
/// </summary>
/// <remarks>
/// The call is idempotent. A shopper holds at most one live subscription per plan, so repeating the
/// request (a double-clicked button, a client retry) returns the existing subscription with
/// <c>alreadySubscribed = true</c> instead of enrolling them twice.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                request.Subscriber = SubscriberIdentityFactory.FromPrincipal(
                    user, request.FirstName, request.LastName, request.Organization);

                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (request.Subscriber is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateSubscriptionRequest.PlanHandle)] = new[] { "A plan handle is required." }
            });
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await subscriptionService.SubscribeAsync(
            new SubscribeRequest(request.Subscriber, request.PlanHandle), cancellationToken);

        response.Subscription = SubscriptionDto.From(result.Subscription);
        response.AlreadySubscribed = result.AlreadyExisted;

        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created(
                $"api/my-subscriptions#{result.Subscription.Id.ToString(CultureInfo.InvariantCulture)}", response);
    }
}
