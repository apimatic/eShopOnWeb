using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to. Optional: the configured default plan is used when omitted.</summary>
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>True when the user already had this subscription (repeat or double-click) and nothing new was created.</summary>
    public bool AlreadySubscribed { get; set; }
}

/// <summary>
/// Subscribes the signed-in user to a plan. Idempotent: repeating the call returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest? request, ClaimsPrincipal principal, IMaxioSubscriptionService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request ?? new CreateSubscriptionRequest(), principal, service, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    // Required by IEndpoint; the route handler above supplies the caller identity and cancellation token.
    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService service)
        => Task.FromResult(Results.Unauthorized());

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal principal,
        IMaxioSubscriptionService service, CancellationToken cancellationToken)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name);
        var user = string.IsNullOrEmpty(userName) ? null : await _userManager.FindByNameAsync(userName);
        if (user is null) return Results.Unauthorized();

        var result = await service.SubscribeAsync(user.Id, user.Email ?? user.UserName ?? userName!, request.PlanHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            AlreadySubscribed = result.AlreadySubscribed,
            Subscription = new SubscriptionDto
            {
                SubscriptionId = result.Subscription.SubscriptionId,
                PlanHandle = result.Subscription.PlanHandle,
                PlanName = result.Subscription.PlanName,
                PriceInCents = result.Subscription.PriceInCents,
                State = result.Subscription.State,
                NextBillingDate = result.Subscription.NextBillingDate
            }
        };
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
