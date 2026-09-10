using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: ensures a single Maxio customer exists
/// for the eShopOnWeb user and never creates a second live subscription to the same plan on a repeat
/// request (e.g. a double-click). Requires a valid JWT; the subscriber is taken from the token.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
                await HandleAsync(request, billingService))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscriber = _httpContextAccessor.HttpContext?.User.ToSubscriber();
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await billingService.SubscribeAsync(subscriber, request.PlanHandle);
        response.Subscription = subscription.ToDto();
        response.AlreadyExisted = subscription.AlreadyExisted;

        // A repeat subscribe returns 200 with the existing subscription; a fresh one returns 201.
        return subscription.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/subscriptions/{subscription.Id}", response);
    }
}
