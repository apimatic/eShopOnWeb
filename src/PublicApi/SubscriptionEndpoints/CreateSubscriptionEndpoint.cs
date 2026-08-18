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
/// Enrolls the authenticated shopper in a Maxio subscription plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest? request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request ?? new CreateSubscriptionRequest(), subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var (shopper, error) = await CurrentShopper.GetAsync(_userManager, principal);
        if (error is not null || shopper is null)
        {
            return error ?? Results.Unauthorized();
        }

        var enrollment = await subscriptionService.SubscribeAsync(new SubscribeShopperRequest
        {
            UserId = shopper.Id,
            Email = shopper.Email ?? shopper.UserName ?? string.Empty,
            UserName = shopper.UserName ?? string.Empty,
            ProductHandle = request.ProductHandle
        });

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ShopperSubscriptionMapping.ToDto(enrollment)
        };

        return Results.Created($"api/subscriptions/{enrollment.Id}", response);
    }
}
