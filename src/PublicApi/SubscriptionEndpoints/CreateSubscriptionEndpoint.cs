using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioService maxioService) =>
            {
                return await HandleAsync(request, maxioService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var user = await _userManager.GetUserAsync(httpContext.User);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            var (maxioCustomerId, _) = await maxioService.EnsureCustomerExistsAsync(
                user.Id,
                user.UserName ?? "User",
                "",
                user.Email ?? "");

            var subscription = await maxioService.CreateSubscriptionAsync(
                user.Id,
                maxioCustomerId,
                request.ProductHandle);

            response.Subscription = new SubscriptionResponseDto
            {
                SubscriptionId = subscription.SubscriptionId,
                ProductHandle = subscription.ProductHandle,
                PlanName = subscription.PlanName,
                PriceInCents = subscription.PriceInCents,
                State = subscription.State,
                NextBillingDate = subscription.NextBillingDate,
                CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
