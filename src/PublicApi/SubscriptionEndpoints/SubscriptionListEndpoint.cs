using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionListEndpoint
{
    public static void AddSubscriptionListRoute(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleAsync)
            .Produces<SubscriptionListResponse>()
            .WithName("GetMySubscriptions")
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        IMaxioBillingService billingService)
    {
        var response = new SubscriptionListResponse();

        try
        {
            var userName = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByNameAsync(userName);
            if (user is null)
            {
                return Results.NotFound("User not found");
            }

            if (!user.MaxioCustomerId.HasValue)
            {
                response.Success = true;
                response.Subscriptions = new();
                return Results.Ok(response);
            }

            var subscriptions = await billingService.GetCustomerSubscriptionsAsync(user.MaxioCustomerId.Value);

            response.Success = true;
            response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                CustomerId = s.CustomerId,
                ProductId = s.ProductId,
                State = s.State,
                CurrentPeriodStartsAt = s.CurrentPeriodStartsAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                TotalRecurringCustom = s.TotalRecurringCustom
            }).ToList();

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = ex.Message;
            return Results.BadRequest(response);
        }
    }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public decimal? TotalRecurringCustom { get; set; }
}

public class SubscriptionListResponse : BaseResponse
{
    public bool Success { get; set; }
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
    public string? Error { get; set; }
}
