using System.Net;
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
/// Enrolls the caller in a Maxio subscription plan. Idempotent for the same plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionApiRequest, ISubscriptionBillingService>
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
            async (CreateSubscriptionApiRequest request, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, billing);
            })
            .Produces<CreateSubscriptionApiResponse>((int)HttpStatusCode.Created)
            .Produces<CreateSubscriptionApiResponse>((int)HttpStatusCode.OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionApiRequest request, ISubscriptionBillingService billing)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var shopper = await ShopperResolver.FromUserAsync(_userManager, user);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var response = new CreateSubscriptionApiResponse(request.CorrelationId());
        var subscription = await billing.SubscribeAsync(shopper, request.ProductHandle);
        response.Subscription = SubscriptionDto.From(subscription);
        response.AlreadySubscribed = subscription.AlreadyExisted;

        return subscription.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
