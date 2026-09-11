using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionsEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpAccessor;

    public SubscriptionsEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpAccessor)
    {
        _userManager = userManager;
        _httpAccessor = httpAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest req, ISubscriptionService service) =>
        {
            return await HandleAsync(req, service);
        })
        .Produces<CreateSubscriptionResponse>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService service)
    {
        var http = _httpAccessor.HttpContext;
        if (http == null) return Results.Unauthorized();
        var userName = http.User?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
            return Results.Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
            return Results.Unauthorized();

        var email = user.Email ?? userName;
        var customer = await service.EnsureCustomerAsync(email);

        var summary = await service.CreateSubscriptionAsync(
            customer.Id,
            customer.Reference,
            request.ProductHandle ?? "eshop-pro");

        var response = new CreateSubscriptionResponse()
        {
            SubscriptionId = summary.Id,
            ProductHandle = summary.Handle,
            State = summary.State,
            NextBillingDate = summary.NextBillingDate,

        };
        return Results.Ok(response);
    }
}
