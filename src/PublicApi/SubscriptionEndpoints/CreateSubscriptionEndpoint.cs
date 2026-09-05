using System;
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

public class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, UserManager<ApplicationUser> userManager, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, userManager, subscriptionService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, UserManager<ApplicationUser> userManager, IMaxioSubscriptionService subscriptionService, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

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

            var subscription = await subscriptionService.CreateSubscriptionAsync(
                maxioCustomer.Id,
                request.PlanHandle
            );

            response.Success = true;
            response.SubscriptionId = subscription.Id;
            response.State = subscription.State;
            response.ProductName = subscription.ProductName;
            response.NextBillingDate = subscription.CurrentPeriodEndsAt;
            response.Message = "Subscription created successfully";

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Error creating subscription: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = "";
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public DateTime? NextBillingDate { get; set; }
}
