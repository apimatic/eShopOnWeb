using System.Security.Claims;
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

/// <summary>
/// Ensures a Maxio customer exists for the authenticated user and enrolls them into a plan.
/// Idempotent: re-posting the same plan for the same user returns the existing subscription
/// instead of creating a duplicate.
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
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionService maxioSubscriptionService) =>
            {
                request.Username = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, maxioSubscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService maxioSubscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required. Choose one from GET api/subscription-plans.");
        }

        var user = await _userManager.FindByNameAsync(request.Username);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var email = user.Email ?? user.UserName!;

        var subscription = await maxioSubscriptionService.SubscribeAsync(request.Username, email, request.PlanHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = new SubscriptionDto
            {
                MaxioSubscriptionId = subscription.MaxioSubscriptionId,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                Price = subscription.Price,
                State = subscription.State,
                NextBillingDate = subscription.NextBillingDate,
                CreatedAt = subscription.CreatedAt
            }
        };

        return subscription.IsNewlyCreated
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
