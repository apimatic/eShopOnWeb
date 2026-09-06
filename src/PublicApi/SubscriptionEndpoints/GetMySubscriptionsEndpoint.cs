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
public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public GetMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, IMaxioService maxioService) =>
            {
                return await HandleAsync(new GetMySubscriptionsRequest(), maxioService, httpContext);
            })
            .Produces<GetMySubscriptionsResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioService maxioService, HttpContext httpContext)
    {
        var response = new GetMySubscriptionsResponse(request.CorrelationId());

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

            var subscriptions = await maxioService.GetUserSubscriptionsAsync(user.Id, maxioCustomerId);

            foreach (var sub in subscriptions)
            {
                response.Subscriptions.Add(new SubscriptionResponseDto
                {
                    SubscriptionId = sub.SubscriptionId,
                    ProductHandle = sub.ProductHandle,
                    PlanName = sub.PlanName,
                    PriceInCents = sub.PriceInCents,
                    State = sub.State,
                    NextBillingDate = sub.NextBillingDate,
                    CurrentPeriodStartsAt = sub.CurrentPeriodStartsAt,
                    CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt
                });
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
