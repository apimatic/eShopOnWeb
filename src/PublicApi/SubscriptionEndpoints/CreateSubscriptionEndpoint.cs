using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.NoContent());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, IMaxioService maxioService,
                   CatalogContext catalogContext,
                   UserManager<ApplicationUser> userManager,
                   HttpContext httpContext) =>
            {
                try
                {
                    var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                    if (string.IsNullOrEmpty(userName))
                    {
                        return Results.BadRequest(new { success = false, message = "Unable to determine current user" });
                    }

                    var user = await userManager.FindByNameAsync(userName);
                    if (user == null)
                    {
                        return Results.BadRequest(new { success = false, message = "User not found" });
                    }

                    var maxioResponse = await maxioService.CreateSubscriptionAsync(user.Id, user.Email ?? "", request.PlanId);

                    var subscription = new Subscription
                    {
                        UserId = user.Id,
                        SubscriptionPlanId = request.PlanId,
                        MaxioSubscriptionId = maxioResponse.SubscriptionId,
                        MaxioCustomerId = maxioResponse.CustomerId,
                        Status = maxioResponse.Status,
                        CreatedAt = DateTime.UtcNow,
                        NextBillingDate = maxioResponse.NextBillingDate,
                        CurrentPrice = 0
                    };

                    catalogContext.Subscriptions.Add(subscription);
                    await catalogContext.SaveChangesAsync();

                    var response = new CreateSubscriptionResponse(Guid.NewGuid())
                    {
                        Success = true,
                        Message = "Subscription created successfully",
                        Subscription = new SubscriptionDto
                        {
                            Id = subscription.Id,
                            SubscriptionPlanId = subscription.SubscriptionPlanId,
                            Status = subscription.Status,
                            CreatedAt = subscription.CreatedAt,
                            NextBillingDate = subscription.NextBillingDate,
                            CurrentPrice = subscription.CurrentPrice
                        }
                    };

                    return Results.Created($"api/subscriptions/{subscription.Id}", response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { success = false, message = $"Failed to create subscription: {ex.Message}" });
                }
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public int PlanId { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public SubscriptionDto? Subscription { get; set; }
}
