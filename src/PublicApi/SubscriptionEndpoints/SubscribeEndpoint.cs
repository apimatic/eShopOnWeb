using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for the
/// eShopOnWeb user (idempotent) and enrolls them, then confirms plan/price/state/next-billing
/// date. Safe against double-submits: repeated calls return the existing subscription instead
/// of creating a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest? request, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(request ?? new SubscribeRequest(), billingService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService billingService)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var cancellationToken = httpContext?.RequestAborted ?? CancellationToken.None;

        var subscriber = httpContext is null
            ? null
            : await CurrentSubscriberResolver.ResolveAsync(httpContext.User, _userManager);

        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var result = await billingService.SubscribeAsync(new SubscribeCommand
        {
            UserReference = subscriber.Reference,
            Email = subscriber.Email,
            PlanHandle = request.PlanHandle
        }, cancellationToken);

        var dto = result.Subscription.ToDto();
        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = dto,
            AlreadyExisted = result.AlreadyExisted,
            Message = result.AlreadyExisted
                ? $"You are already subscribed to {dto.PlanName} ({dto.FormattedPrice}). State: {dto.State}."
                : $"Subscribed to {dto.PlanName} ({dto.FormattedPrice}). State: {dto.State}."
        };

        // New subscription -> 201 Created; existing (idempotent) -> 200 OK.
        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions/{dto.SubscriptionId}", response);
    }
}
