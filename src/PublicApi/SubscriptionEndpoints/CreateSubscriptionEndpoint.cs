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
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: the Maxio
/// customer is looked up (or created) by the eShopOnWeb user id, and when a
/// live subscription to the same plan already exists it is returned instead
/// of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal>
{
    private readonly SubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        SubscriptionBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken) =>
            {
                return await HandleInternalAsync(request, claimsPrincipal, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal) =>
        HandleInternalAsync(request, claimsPrincipal, CancellationToken.None);

    private async Task<IResult> HandleInternalAsync(CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var user = await GetCurrentUserAsync(claimsPrincipal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var plan = await _billingService.FindPlanAsync(request.ProductHandle, cancellationToken);
        if (plan is null)
        {
            return Results.NotFound($"No subscription plan with handle '{request.ProductHandle}'.");
        }

        var customer = await _billingService.GetOrCreateCustomerAsync(user, cancellationToken);

        var existing = await _billingService.FindLiveSubscriptionAsync(customer.Id, plan.Handle!, cancellationToken);
        if (existing is not null)
        {
            response.Subscription = SubscriptionMapper.ToDto(existing);
            response.Created = false;
            return Results.Ok(response);
        }

        var subscription = await _billingService.CreateSubscriptionAsync(customer, user, plan.Handle!, cancellationToken);

        response.Subscription = SubscriptionMapper.ToDto(subscription);
        response.Created = true;
        return Results.Created("api/my-subscriptions", response);
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal)
    {
        var userName = claimsPrincipal.Identity?.Name;
        return string.IsNullOrEmpty(userName) ? null : await _userManager.FindByNameAsync(userName);
    }
}
