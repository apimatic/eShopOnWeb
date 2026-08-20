using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enroll the authenticated shopper in a Maxio subscription plan.
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
            async (CreateSubscriptionRequest? request, ISubscriptionBillingService billing, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request ?? new CreateSubscriptionRequest(), billing, user, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
        => HandleAsync(request, billing, new ClaimsPrincipal(), CancellationToken.None);

    private async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var shopper = await ResolveShopperAsync(principal);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var (firstName, lastName) = ShopperIdentity.SplitDisplayName(shopper);
        var email = shopper.Email ?? shopper.UserName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.BadRequest("The authenticated user does not have an email address.");
        }

        var result = await billing.SubscribeAsync(
            new SubscribeRequest(shopper.Id, email, firstName, lastName, request.ProductHandle),
            cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionMapper.ToDto(result)
        };

        return result.Created
            ? Results.Created($"api/subscriptions/{result.Id}", response)
            : Results.Ok(response);
    }

    private async Task<ApplicationUser?> ResolveShopperAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(userName);
    }
}
