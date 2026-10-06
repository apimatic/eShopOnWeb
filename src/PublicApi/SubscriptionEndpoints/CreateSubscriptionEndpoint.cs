using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan in the billing system of record.
/// The flow is idempotent: a double-click resolves to the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService, ClaimsPrincipal>
{
    private const int MAX_IDEMPOTENCY_KEY_LENGTH = 255;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, subscriptionService, user);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.PlanHandle)] = ["A plan handle is required. Browse available plans with GET api/subscription-plans."],
            });
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (idempotencyKey is { Length: > MAX_IDEMPOTENCY_KEY_LENGTH })
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.IdempotencyKey)] = [$"The idempotency key must not exceed {MAX_IDEMPOTENCY_KEY_LENGTH} characters."],
            });
        }

        var shopperEmail = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(shopperEmail))
        {
            return Results.Unauthorized();
        }

        var outcome = await subscriptionService.SubscribeAsync(
            new SubscribeRequest(
                UserReference: shopperEmail,
                Email: shopperEmail,
                FirstName: request.FirstName,
                LastName: request.LastName,
                PlanHandle: request.PlanHandle.Trim(),
                IdempotencyKey: idempotencyKey));

        response.Subscription = SubscriptionApiMapper.ToDto(outcome.Subscription);
        response.WasNewlyCreated = outcome.WasNewlyCreated;

        return outcome.WasNewlyCreated
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
