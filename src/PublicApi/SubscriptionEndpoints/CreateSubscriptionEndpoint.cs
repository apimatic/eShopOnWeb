using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, IMaxioService maxioService, IRepository<Subscription> subscriptionRepository, IHttpContextAccessor httpContextAccessor) =>
            {
                return await Handle(request, maxioService, subscriptionRepository, httpContextAccessor);
            })
           .Produces<CreateSubscriptionResponse>()
           .Accepts<CreateSubscriptionRequest>("application/json")
           .WithTags("SubscriptionEndpoints");
    }

    private async Task<IResult> Handle(
        CreateSubscriptionRequest request,
        IMaxioService maxioService,
        IRepository<Subscription> subscriptionRepository,
        IHttpContextAccessor httpContextAccessor)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Results.Unauthorized();
        }

        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(userEmail))
        {
            return Results.BadRequest(new { error = "User email not found in token" });
        }

        var userFirstName = httpContext.User.FindFirst("first_name")?.Value ?? "User";
        var userLastName = httpContext.User.FindFirst("last_name")?.Value ?? userId;

        try
        {
            var maxioSubscription = await maxioService.GetOrCreateCustomerAndSubscribeAsync(
                userId,
                userFirstName,
                userLastName,
                userEmail,
                request.PlanHandle);

            var dbSubscription = new Subscription
            {
                UserId = userId,
                MaxioSubscriptionId = maxioSubscription.Id,
                MaxioCustomerId = maxioSubscription.CustomerId,
                PlanHandle = maxioSubscription.ProductHandle ?? request.PlanHandle,
                State = maxioSubscription.State,
                PriceInCents = maxioSubscription.PriceInCents,
                NextBillingAt = maxioSubscription.NextBillingAt,
                CreatedAt = maxioSubscription.CreatedAt,
                UpdatedAt = maxioSubscription.UpdatedAt,
            };

            await subscriptionRepository.AddAsync(dbSubscription);

            return Results.Ok(new CreateSubscriptionResponse
            {
                SubscriptionId = maxioSubscription.Id,
                CustomerId = maxioSubscription.CustomerId,
                PlanHandle = maxioSubscription.ProductHandle ?? request.PlanHandle,
                State = maxioSubscription.State,
                PricePerMonth = maxioSubscription.PriceInCents / 100m,
                NextBillingAt = maxioSubscription.NextBillingAt,
                Message = $"Successfully subscribed to {request.PlanHandle}"
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string PlanHandle { get; set; }
    public string State { get; set; }
    public decimal PricePerMonth { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public string Message { get; set; }
}
