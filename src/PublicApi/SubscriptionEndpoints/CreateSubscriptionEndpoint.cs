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
/// Enrolls the caller in a Maxio subscription plan. Idempotent for an existing live subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ISubscriptionBillingService billing, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, billing, user);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing) =>
        throw new System.NotSupportedException("Use HandleAsync(request, billing, user).");

    private async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing,
        ClaimsPrincipal user)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { StatusCode = 400, Message = "productHandle is required." });
        }

        var shopper = await user.ToShopperAsync(_userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await billing.SubscribeAsync(shopper, request.ProductHandle);
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription.ToDto(),
            AlreadySubscribed = subscription.AlreadyExisted
        };

        if (subscription.AlreadyExisted)
        {
            return Results.Ok(response);
        }

        return Results.Created($"api/my-subscriptions", response);
    }
}
