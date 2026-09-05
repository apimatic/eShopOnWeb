using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
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

/// <summary>
/// Subscribes the calling user to a Maxio plan: ensures a Maxio customer exists for them
/// (idempotent on the user id), then enrolls them in the requested plan (idempotent on
/// user+plan -- a retry returns the existing subscription rather than creating a second one).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IMaxioBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal callingUser, CancellationToken ct) =>
            {
                return await HandleAsync(request, callingUser, ct);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal callingUser, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "PlanHandle is required."
            });
        }

        // The JWT this API issues carries only a Name claim (see IdentityTokenClaimService) --
        // no NameIdentifier -- so the calling user must be resolved by username.
        var user = await _userManager.FindByNameAsync(callingUser.Identity?.Name ?? string.Empty);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var email = user.Email ?? user.UserName!;
        var subscription = await _billingService.SubscribeAsync(user.Id, email, request.PlanHandle, ct);

        response.Subscription = new SubscriptionDto
        {
            SubscriptionId = subscription.MaxioSubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.Price,
            Currency = subscription.Currency,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate
        };

        return Results.Ok(response);
    }
}
