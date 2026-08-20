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
            async (CreateSubscriptionRequest request, ISubscriptionBillingService billing, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await SubscribeAsync(request, billing, user, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
        => SubscribeAsync(request, billing, new ClaimsPrincipal(), CancellationToken.None);

    private async Task<IResult> SubscribeAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var shopper = await ShopperIdentityResolver.ResolveAsync(_userManager, user);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await billing.SubscribeAsync(shopper, request.ProductHandle, cancellationToken);
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDtoMapper.Map(subscription)
        };
        return Results.Created($"api/subscriptions/{subscription.Id}", response);
    }
}
