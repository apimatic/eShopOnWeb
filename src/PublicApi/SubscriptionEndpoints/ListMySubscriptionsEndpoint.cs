using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's Maxio subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ClaimsPrincipal>
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
            (ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), claimsPrincipal, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ClaimsPrincipal claimsPrincipal)
    {
        return HandleAsync(request, claimsPrincipal, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var username = claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        // Read-only: never provision a customer here. No Maxio customer yet means no subscriptions.
        var customer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is null)
        {
            return Results.Ok(response);
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        response.Subscriptions = subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(CreateSubscriptionEndpoint.Map)
            .ToList();

        return Results.Ok(response);
    }
}
