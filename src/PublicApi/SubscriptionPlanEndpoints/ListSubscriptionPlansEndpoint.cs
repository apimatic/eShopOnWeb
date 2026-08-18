using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// Lists Maxio subscription plans for the configured product family.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListSubscriptionPlansEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var (_, error) = await CurrentShopper.GetAsync(_userManager, principal);
        if (error is not null)
        {
            return error;
        }

        var response = new ListSubscriptionPlansResponse();
        var plans = await subscriptionService.ListPlansAsync();
        response.Plans.AddRange(plans.Select(plan => new SubscriptionPlanDto
        {
            Id = plan.Id,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            Price = plan.Price,
            PriceInCents = plan.PriceInCents,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            ProductFamilyHandle = plan.ProductFamilyHandle,
            RequireCreditCard = plan.RequireCreditCard
        }));

        return Results.Ok(response);
    }
}
