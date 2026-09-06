using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            CreateSubscription)
           .Produces<CreateSubscriptionResponse>()
           .WithName("CreateSubscription")
           .WithTags("Subscriptions")
           .RequireAuthorization();
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    private static async Task<IResult> CreateSubscription(CreateSubscriptionRequest request, ClaimsPrincipal user,
        IRepository<UserMaxioCustomer> userCustomerRepository,
        IRepository<UserSubscription> userSubscriptionRepository,
        IMaxioApiClient maxioApiClient)
    {
        var response = new CreateSubscriptionResponse();

        try
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                response.Error = "User not found";
                return Results.Unauthorized();
            }

            var userEmail = user.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var userName = user.FindFirst("name")?.Value ?? "";
            var nameParts = userName.Split(" ", StringSplitOptions.RemoveEmptyEntries);
            var firstName = nameParts.Length > 0 ? nameParts[0] : "";
            var lastName = nameParts.Length > 1 ? nameParts[1] : "";

            // Get or create Maxio customer
            var existingCustomer = await userCustomerRepository.FirstOrDefaultAsync(new UserCustomerByUserIdSpec(userId));
            int maxioCustomerId;

            if (existingCustomer != null)
            {
                maxioCustomerId = existingCustomer.MaxioCustomerId;
            }
            else
            {
                var customer = await maxioApiClient.CreateOrGetCustomerAsync(userId, userEmail, firstName, lastName);
                if (customer == null)
                {
                    response.Error = "Failed to create customer in billing system";
                    return Results.BadRequest(response);
                }

                maxioCustomerId = customer.Id;

                // Save the customer mapping locally
                var userCustomer = new UserMaxioCustomer
                {
                    UserId = userId,
                    MaxioCustomerId = maxioCustomerId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await userCustomerRepository.AddAsync(userCustomer);
            }

            // Create subscription
            var subscription = await maxioApiClient.CreateSubscriptionAsync(maxioCustomerId, request.ProductHandle);
            if (subscription == null)
            {
                response.Error = "Failed to create subscription";
                return Results.BadRequest(response);
            }

            // Save subscription mapping locally
            var userSubscription = new UserSubscription
            {
                UserId = userId,
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = request.ProductHandle,
                State = subscription.State,
                NextBillingAt = subscription.NextAssessmentAt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await userSubscriptionRepository.AddAsync(userSubscription);

            response.Success = true;
            response.SubscriptionId = subscription.Id;
            response.State = subscription.State;
            response.ProductName = subscription.Product?.Name ?? request.ProductHandle;
            response.NextBillingDate = subscription.NextAssessmentAt;

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.Error = $"Error creating subscription: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public bool Success { get; set; }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public string? Error { get; set; }
}

