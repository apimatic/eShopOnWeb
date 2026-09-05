using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's Maxio subscriptions. Read-only: if the user has never
/// subscribed (no Maxio customer yet), returns an empty list rather than creating one.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(IMaxioClient maxioClient, UserManager<ApplicationUser> userManager)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal principal)
    {
        var response = new ListMySubscriptionsResponse();

        var appUser = await _userManager.FindByNameAsync(principal.Identity!.Name!);
        if (appUser is null)
        {
            return Results.Unauthorized();
        }

        var customer = await _maxioClient.FindCustomerByReferenceAsync(appUser.Id);
        if (customer is null)
        {
            return Results.Ok(response);
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id);
        response.Subscriptions = subscriptions.Select(SubscriptionMapper.ToDto).ToList();
        return Results.Ok(response);
    }
}
