using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the authenticated caller.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(IBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user) => await HandleAsync(user))
            .RequireAuthorization(SubscriptionAuth.JwtPolicy)
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal principal)
    {
        var currentUser = await CurrentUserResolver.ResolveAsync(principal, _userManager);
        if (currentUser is null)
        {
            return Results.Problem(title: "Unauthorized", detail: "The caller could not be resolved to a user.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var response = new ListMySubscriptionsResponse();
        try
        {
            var subscriptions = await _billingService.GetSubscriptionsAsync(currentUser.UserReference);
            response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();
            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return BillingProblem.From(ex);
        }
    }
}
