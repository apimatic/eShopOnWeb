using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, UserManager<ApplicationUser> userManager, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(userManager, subscriptionService, httpContext);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(UserManager<ApplicationUser> userManager, IMaxioSubscriptionService subscriptionService, HttpContext httpContext)
    {
        var response = new GetMySubscriptionsResponse(Guid.NewGuid());

        try
        {
            var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(username))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByNameAsync(username);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            var maxioCustomer = await subscriptionService.GetOrCreateCustomerAsync(
                user.Id,
                user.Email ?? user.UserName ?? "",
                user.UserName ?? "Unknown",
                "User"
            );

            var subscriptions = await subscriptionService.GetCustomerSubscriptionsAsync(maxioCustomer.Id);

            response.Subscriptions.AddRange(subscriptions.Select(s => new MySubscriptionResponse
            {
                Id = s.Id,
                State = s.State,
                ProductName = s.ProductName,
                ActivatedAt = s.ActivatedAt,
                NextBillingDate = s.CurrentPeriodEndsAt,
                CanceledAt = s.CanceledAt
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public List<MySubscriptionResponse> Subscriptions { get; set; } = new();
}

public class MySubscriptionResponse
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public DateTime? ActivatedAt { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CanceledAt { get; set; }
}
