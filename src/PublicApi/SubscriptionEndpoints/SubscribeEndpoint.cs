using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// POST /api/subscriptions — enrolls the authenticated shopper in a plan. Idempotent: ensures a
/// Maxio customer exists for the user and reuses an existing live subscription for the plan
/// rather than creating a duplicate, so a double-click never creates two subscriptions.
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeSubscriptionRequest request, HttpContext httpContext,
             ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
                await HandleAsync(httpContext.User, request, billingService, cancellationToken))
            .Produces<SubscribeSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Subscribe to a plan",
                description: "Subscribes the authenticated user to the requested plan (by handle)."));
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, SubscribeSubscriptionRequest request,
        ISubscriptionBillingService billingService, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new SubscriptionBillingException(
                "A 'planHandle' is required. Choose one from GET /api/subscription-plans.", 400);
        }

        var subscribeRequest = SubscriptionCaller.ToSubscribeRequest(user, request.PlanHandle.Trim());
        var result = await billingService.SubscribeAsync(subscribeRequest, cancellationToken);

        var response = new SubscribeSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            AlreadyExisted = result.AlreadyExisted
        };

        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions#{response.Subscription.Id}", response);
    }
}

public class SubscribeSubscriptionRequest : BaseRequest
{
    /// <summary>The API handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;
}

public class SubscribeSubscriptionResponse : BaseResponse
{
    public SubscribeSubscriptionResponse(System.Guid correlationId) : base(correlationId) { }

    public SubscribeSubscriptionResponse() { }

    public CustomerSubscriptionDto Subscription { get; set; } = new();

    /// <summary>True when the user was already subscribed and no new subscription was created.</summary>
    public bool AlreadyExisted { get; set; }
}
