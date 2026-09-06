using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListMySubscriptionsEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext) =>
            {
                var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(userIdClaim.Value);
            })
           .Produces<ListMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints")
           .WithName("GetMySubscriptions");
    }

    public async Task<IResult> HandleAsync()
    {
        return Results.BadRequest("This method should not be called directly");
    }

    private async Task<IResult> HandleAsync(string userId)
    {
        try
        {
            var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(userId);
            var response = new ListMySubscriptionsResponse { Subscriptions = subscriptions };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public decimal PricePerBillingCycle { get; set; }
    public int BillingIntervalDays { get; set; }
    public string? BillingInterval { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CreatedAt { get; set; }
}

public class ListMySubscriptionsResponse
{
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}
