using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a subscription plan.
/// Idempotent: repeated calls for the same plan return the existing subscription.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscribeEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, IMaxioBillingService billingService) =>
            {
                var appUser = await _userManager.FindByNameAsync(user.Identity?.Name ?? string.Empty);
                if (appUser is null)
                {
                    return Results.Unauthorized();
                }

                request.UserId = appUser.Id;
                request.Email = appUser.Email ?? appUser.UserName ?? string.Empty;
                return await HandleAsync(request, billingService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("PlanHandle is required.");
        }

        var response = new SubscribeResponse(request.CorrelationId());

        try
        {
            var subscription = await billingService.SubscribeAsync(request.UserId, request.Email, request.PlanHandle);
            response.Subscription = Map(subscription);
            return Results.Ok(response);
        }
        catch (PlanNotFoundException ex)
        {
            return Results.NotFound(ex.Message);
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    internal static SubscriptionDto Map(ApplicationCore.Models.SubscriptionDetails s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        State = s.State,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        MaxioCustomerId = s.MaxioCustomerId,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextAssessmentAt = s.NextAssessmentAt
    };
}
