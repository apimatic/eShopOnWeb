using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan (the hero flow). Ensures a Maxio customer exists for
/// the shopper (idempotent by user id) and does not create a second subscription on a double-click.
/// POST /api/subscriptions
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly ISubscriptionBillingService _billing;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscribeEndpoint(ISubscriptionBillingService billing, UserManager<ApplicationUser> userManager)
    {
        _billing = billing;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, CancellationToken ct) => await HandleAsync(request, user, ct))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new SubscriptionEndpointSupport.ProblemPayload("planHandle is required."));
        }

        var subscriber = await SubscriptionEndpointSupport.ResolveSubscriberAsync(user, _userManager);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await _billing.SubscribeAsync(subscriber, request.PlanHandle.Trim(), ct);
            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = result.Subscription.ToDto(),
                AlreadyExisted = result.AlreadyExisted
            };

            // A brand-new enrollment is a create; an idempotent no-op returns 200 with the existing one.
            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/subscriptions/{response.Subscription.Id}", response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionEndpointSupport.ToProblem(ex);
        }
    }
}
