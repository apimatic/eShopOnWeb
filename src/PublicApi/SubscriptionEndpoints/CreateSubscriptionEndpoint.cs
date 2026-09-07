using System;
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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionManager>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ISubscriptionManager manager, HttpContext httpContext) =>
            {
                return await HandleAsync(request, manager, httpContext);
            })
           .Produces<CreateSubscriptionResponse>()
           .Accepts<CreateSubscriptionRequest>("application/json")
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionManager manager)
    {
        return Results.Ok();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionManager manager, HttpContext httpContext)
    {
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail))
                return Results.BadRequest(new { error = "User email not found in token" });

            var userFirstName = httpContext.User.FindFirst("first_name")?.Value ?? "User";
            var userLastName = httpContext.User.FindFirst("last_name")?.Value ?? userId;

            var subscription = await manager.CreateSubscriptionAsync(request.PlanHandle, userId, userEmail, userFirstName, userLastName);
            return Results.Ok(new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.SubscriptionId,
                CustomerId = subscription.CustomerId,
                PlanHandle = subscription.PlanHandle,
                State = subscription.State,
                PricePerMonth = subscription.PricePerMonth,
                NextBillingAt = subscription.NextBillingAt,
                Message = subscription.Message
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
    public System.DateTime? NextBillingAt { get; set; }
    public string Message { get; set; }
}
