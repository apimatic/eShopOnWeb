using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionPlanRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionPlansEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (SubscriptionPlanRequest req, ISubscriptionService service) =>
        {
            return await HandleAsync(req, service);
        })
        .Produces<SubscriptionPlanResponse>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanRequest request, ISubscriptionService service)
    {
        var response = new SubscriptionPlanResponse();
        var plans = await service.GetPlansAsync();
        response.Plans = plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Price = p.Price,
            FamilyHandle = p.FamilyHandle
        }).ToArray();
        return Results.Ok(response);
    }
}
