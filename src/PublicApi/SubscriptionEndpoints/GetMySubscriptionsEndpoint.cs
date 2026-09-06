using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (MaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var endpoint = new GetMySubscriptionsEndpoint();
                return await endpoint.HandleAsyncInternal(subscriptionService, httpContext);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService subscriptionService)
    {
        throw new NotImplementedException("Use the private overload with HttpContext");
    }

    private async Task<IResult> HandleAsyncInternal(MaxioSubscriptionService subscriptionService, HttpContext httpContext)
    {
        var response = new GetMySubscriptionsResponse();

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var customer = await subscriptionService.GetOrCreateCustomerAsync(
                userId,
                httpContext.User.FindFirst("first_name")?.Value ?? "User",
                httpContext.User.FindFirst("last_name")?.Value ?? "Account",
                httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? "user@example.com");

            var subscriptions = await subscriptionService.GetSubscriptionsAsync((int)customer.Id);
            response.Subscriptions.AddRange(subscriptions.Select(s => new UserSubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                ProductHandle = s.ProductHandle,
                ProductName = s.ProductName,
                NextBillingDate = s.NextAssessmentAt,
                CurrentBillingAmountInCents = s.CurrentBillingAmountInCents
            }));

            return Results.Ok(response);
        }
        catch (MaxioServiceException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; } = new();
}

public class UserSubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public System.DateTimeOffset? NextBillingDate { get; set; }
    public long CurrentBillingAmountInCents { get; set; }

    public decimal GetCurrentBillingAmount() => CurrentBillingAmountInCents / 100m;
}
