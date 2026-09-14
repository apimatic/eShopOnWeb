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

public sealed class MySubscriptionListEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IMaxioBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionListEndpoint(
        IMaxioBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal principal, CancellationToken cancellationToken) =>
                    await HandleAsync(principal, cancellationToken))
            .Produces<UserSubscriptionListResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await _billingService.GetSubscriptionsAsync(user.Id, cancellationToken);
            return Results.Ok(new UserSubscriptionListResponse(subscriptions));
        }
        catch (MaxioApiException exception)
        {
            return SubscriptionEndpointHelpers.BillingFailure(exception);
        }
    }

    Task<IResult> IEndpoint<IResult, ClaimsPrincipal>.HandleAsync(ClaimsPrincipal principal)
        => HandleAsync(principal, CancellationToken.None);
}
