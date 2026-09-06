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
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(
                Summary = "Create a new subscription",
                Description = "Create a subscription to a plan for the current user",
                OperationId = "subscriptions.create",
                Tags = new[] { "SubscriptionEndpoints" })]
            async (CreateSubscriptionRequest request, HttpContext httpContext,
                MaxioClient maxioClient,
                UserManager<ApplicationUser> userManager,
                IRepository<UserSubscription> subscriptionRepository) =>
            {
                return await HandleAsync(request, httpContext, maxioClient, userManager, subscriptionRepository);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(400)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext,
        MaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        IRepository<UserSubscription> subscriptionRepository)
    {
        var response = new CreateSubscriptionResponse();

        try
        {
            var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(userName))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByNameAsync(userName);
            if (user == null)
            {
                return Results.NotFound(new { error = "User not found" });
            }

            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                return Results.BadRequest(new { error = "Product handle is required" });
            }

            var customer = await maxioClient.GetOrCreateCustomerAsync(
                user.Email ?? userName,
                user.Email?.Split('@')[0] ?? "Customer",
                "User",
                user.Id);

            var subscription = await maxioClient.CreateSubscriptionAsync(customer.Id, request.ProductHandle);

            var userSubscription = new UserSubscription
            {
                UserId = user.Id,
                MaxioCustomerId = customer.Id,
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = request.ProductHandle,
                CreatedAt = DateTime.UtcNow
            };

            await subscriptionRepository.AddAsync(userSubscription);

            response.Subscription = new UserSubscriptionDto
            {
                Id = subscription.Id,
                ProductHandle = subscription.ProductHandle,
                ProductName = subscription.Product.Name,
                Price = subscription.Product.Price,
                State = subscription.State,
                NextBillingAt = subscription.NextBillingAt,
                CreatedAt = subscription.CreatedAt
            };

            response.Message = "Subscription created successfully";
            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = "";
}

public class CreateSubscriptionResponse : BaseResponse
{
    public UserSubscriptionDto? Subscription { get; set; }
    public string Message { get; set; } = "";
}
