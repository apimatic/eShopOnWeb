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

public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpAccessor;

    public MySubscriptionsEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpAccessor)
    {
        _userManager = userManager;
        _httpAccessor = httpAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (MySubscriptionsRequest req, ISubscriptionService service) =>
        {
            return await HandleAsync(req, service);
        })
        .Produces<MySubscriptionsResponse>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService service)
    {
        var http = _httpAccessor.HttpContext;
        if (http == null) return Results.Unauthorized();
        var userName = http.User?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName)) return Results.Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null) return Results.Unauthorized();

        var email = user.Email ?? userName;
        var customer = await service.EnsureCustomerAsync(email);
        var subs = await service.ListCustomerSubscriptionsAsync(customer.Id);

        var response = new MySubscriptionsResponse()
        {
            Subscriptions = subs.Select(s => new MySubscriptionDto
            {
                Id = s.Id,
                Handle = s.Handle,
                State = s.State,
                NextBillingDate = s.NextBillingDate
            }).ToArray()
        };
        return Results.Ok(response);
    }
}
