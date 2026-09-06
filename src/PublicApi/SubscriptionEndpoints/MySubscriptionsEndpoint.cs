using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ISubscriptionService subscriptionService, HttpContext context) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(), subscriptionService, context);
            })
            .RequireAuthorization()
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());
        return Results.Problem("Not implemented");
    }

    private async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService, HttpContext context)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        try
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var appUser = await _userManager.FindByIdAsync(userId);
            if (appUser == null)
            {
                return Results.NotFound();
            }

            var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(appUser);
            response.Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
            {
                Id = s.Id,
                PlanHandle = s.PlanHandle,
                State = s.State,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt
            }).ToArray();

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 500);
        }
    }
}

public class MySubscriptionsRequest : BaseRequest
{
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public UserSubscriptionDto[] Subscriptions { get; set; } = System.Array.Empty<UserSubscriptionDto>();
}
