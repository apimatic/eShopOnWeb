using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists Maxio subscriptions belonging to the authenticated shopper.</summary>
public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioBillingService, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioBillingService billing, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(billing, userManager);
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService billing, UserManager<ApplicationUser> userManager)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var user = principal is null ? null : await SubscribeEndpoint.GetUserAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billing.GetSubscriptionsAsync(user, _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
            return Results.Ok(new MySubscriptionsResponse { Subscriptions = subscriptions.ToList() });
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The billing service could not retrieve subscriptions.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
