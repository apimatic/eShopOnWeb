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
/// Enrolls the signed-in shopper in a Maxio subscription plan. Idempotent for a given user + plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateShopperSubscriptionRequest, ISubscriptionBillingService>
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
            async (CreateShopperSubscriptionRequest request, ISubscriptionBillingService billing, HttpContext http, CancellationToken ct) =>
            {
                return await CreateAsync(request, billing, http, ct);
            })
            .Produces<CreateShopperSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateShopperSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateShopperSubscriptionRequest request, ISubscriptionBillingService billing)
        => CreateAsync(request, billing, new DefaultHttpContext(), CancellationToken.None);

    private async Task<IResult> CreateAsync(
        CreateShopperSubscriptionRequest request,
        ISubscriptionBillingService billing,
        HttpContext http,
        CancellationToken ct)
    {
        var shopper = await ShopperIdentityResolver.ResolveAsync(http, _userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var result = await billing.SubscribeAsync(shopper, request.ProductHandle ?? string.Empty, ct);
        var response = new CreateShopperSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ShopperSubscriptionDtoMapper.From(result.Subscription)
        };

        if (result.Created)
        {
            return Results.Created($"api/subscriptions/{result.Subscription.Id}", response);
        }

        return Results.Ok(response);
    }
}
