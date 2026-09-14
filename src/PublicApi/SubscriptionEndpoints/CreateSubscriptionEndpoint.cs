using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request to subscribe the authenticated user to a plan.</summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Stable handle of the plan to subscribe to (e.g. from GET /api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}

/// <summary>Response confirming the subscription.</summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>The active subscription (plan, price, state, next billing date).</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>True when an existing live subscription was returned (idempotent no-op) rather than newly created.</summary>
    public bool AlreadyExisted { get; set; }
}

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: a double submission never creates a
/// second customer or subscription — an existing live subscription is returned instead.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionAppService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionAppService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionAppService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionAppService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        var result = await subscriptionService.SubscribeAsync(request.PlanHandle, cancellationToken);

        response.Subscription = result.Subscription.ToDto();
        response.AlreadyExisted = result.AlreadyExisted;

        // 200 when we returned an already-existing subscription, 201 when a new one was created.
        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions/{result.Subscription.Id}", response);
    }
}
