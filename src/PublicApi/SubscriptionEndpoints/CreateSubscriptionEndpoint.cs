using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.MaxioIntegration;
using Microsoft.AspNetCore.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest req, ClaimsPrincipal user, IMaxioService maxioService,
                UserManager<ApplicationUser> userManager, IRepository<Subscription> subscriptionRepository) =>
            {
                var response = new CreateSubscriptionResponse();

                try
                {
                    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        response.Success = false;
                        response.ErrorMessage = "User not authenticated";
                        return Results.Unauthorized();
                    }

                    var appUser = await userManager.FindByIdAsync(userId);
                    if (appUser == null)
                    {
                        response.Success = false;
                        response.ErrorMessage = "User not found";
                        return Results.NotFound(response);
                    }

                    var firstName = appUser.UserName?.Split(' ')[0] ?? "User";
                    var lastName = appUser.UserName?.Split(' ').Skip(1).FirstOrDefault() ?? "";

                    // Get or create Maxio customer
                    var customer = await maxioService.GetOrCreateCustomerAsync(userId, appUser.Email!, firstName, lastName);
                    if (customer == null)
                    {
                        response.Success = false;
                        response.ErrorMessage = "Failed to create or retrieve Maxio customer";
                        return Results.BadRequest(response);
                    }

                    // Create subscription in Maxio
                    var maxioSubscription = await maxioService.CreateSubscriptionAsync(customer.Id, req.PlanHandle);

                    // Save subscription to database
                    var subscription = new Subscription
                    {
                        UserId = userId,
                        MaxioCustomerId = customer.Id,
                        MaxioSubscriptionId = maxioSubscription.Id,
                        PlanHandle = maxioSubscription.ProductHandle,
                        State = maxioSubscription.State,
                        CurrentPrice = maxioSubscription.CurrentPrice,
                        NextBillingAt = maxioSubscription.NextAssessmentAt,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    await subscriptionRepository.AddAsync(subscription);

                    response.Success = true;
                    response.SubscriptionId = maxioSubscription.Id;
                    response.PlanHandle = maxioSubscription.ProductHandle;
                    response.State = maxioSubscription.State;
                    response.CurrentPrice = maxioSubscription.CurrentPrice;
                    response.NextBillingAt = maxioSubscription.NextAssessmentAt;

                    return Results.CreatedAtRoute("GetSubscription", new { id = subscription.Id }, response);
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.ErrorMessage = ex.Message;
                    return Results.BadRequest(response);
                }
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints");
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = null!;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public bool Success { get; set; }
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public decimal CurrentPrice { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public string? ErrorMessage { get; set; }
}
