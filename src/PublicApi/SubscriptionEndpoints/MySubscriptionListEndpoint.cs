using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionListEndpoint : IEndpoint<IResult, UserManager<ApplicationUser>>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionListEndpoint(
        ISubscriptionBillingService billingService,
        IHttpContextAccessor httpContextAccessor)
    {
        _billingService = billingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (UserManager<ApplicationUser> userManager) => await HandleAsync(userManager))
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(UserManager<ApplicationUser> userManager)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var user = await SubscriptionEndpointSupport.GetBillingUserAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return await SubscriptionEndpointSupport.ExecuteAsync(async () =>
        {
            var subscriptions = await _billingService.GetSubscriptionsAsync(
                user,
                _httpContextAccessor.HttpContext?.RequestAborted ?? default);
            return Results.Ok(ListMySubscriptionsResponse.From(subscriptions));
        });
    }
}
