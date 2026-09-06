using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext context, UserManager<ApplicationUser> userManager, IMaxioSubscriptionService subscriptionService) =>
            {
                var userId = userManager.GetUserId(context.User);
                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                var user = await userManager.FindByIdAsync(userId);
                if (user == null)
                    return Results.NotFound("User not found");

                if (string.IsNullOrEmpty(request.ProductHandle))
                    return Results.BadRequest("ProductHandle is required");

                try
                {
                    int customerId = user.MaxioCustomerId ?? 0;
                    if (customerId == 0)
                    {
                        customerId = await subscriptionService.EnsureCustomerExistsAsync(
                            userId,
                            user.FirstName ?? "Customer",
                            user.LastName ?? string.Empty,
                            user.Email ?? string.Empty);

                        user.MaxioCustomerId = customerId;
                        await userManager.UpdateAsync(user);
                    }

                    var subscription = await subscriptionService.CreateSubscriptionAsync(customerId, request.ProductHandle);

                    return Results.Ok(new CreateSubscriptionResponse
                    {
                        SubscriptionId = subscription.Id,
                        CustomerId = subscription.CustomerId,
                        State = subscription.State,
                        ProductName = subscription.ProductName,
                        ProductHandle = subscription.ProductHandle,
                        PriceInCents = subscription.ProductPriceInCents,
                        NextBillingDate = subscription.NextAssessmentAt
                    });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException();
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
