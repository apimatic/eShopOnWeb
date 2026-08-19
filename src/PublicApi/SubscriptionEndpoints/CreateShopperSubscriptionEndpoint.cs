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

public class CreateShopperSubscriptionEndpoint : IEndpoint<IResult, CreateShopperSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateShopperSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateShopperSubscriptionRequest request, ISubscriptionBillingService billing, ClaimsPrincipal user) =>
                await HandleForUserAsync(request, billing, user))
            .Produces<CreateShopperSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(
        CreateShopperSubscriptionRequest request,
        ISubscriptionBillingService billing) =>
        HandleForUserAsync(request, billing, new ClaimsPrincipal());

    private async Task<IResult> HandleForUserAsync(
        CreateShopperSubscriptionRequest request,
        ISubscriptionBillingService billing,
        ClaimsPrincipal user)
    {
        var buyer = await ListMySubscriptionsEndpoint.ResolveBuyerAsync(_userManager, user);
        if (buyer is null)
        {
            return Results.Unauthorized();
        }

        var created = await billing.SubscribeAsync(buyer, request.ProductHandle);
        var response = new CreateShopperSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ListMySubscriptionsEndpoint.ToDto(created)
        };

        return Results.Created($"api/subscriptions/{created.Id}", response);
    }
}
