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
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class CreateSubscriptionEndpoint
{
    public static void MapCreateSubscription(this WebApplication app)
    {
        app.MapPost("api/subscriptions", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    private static async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioApiClient maxioClient,
        UserManager<ApplicationUser> userManager, IOptionsSnapshot<MaxioSettings> settings, HttpContext context)
    {
        var userId = context.User.FindFirst(ClaimTypes.Name)?.Value;
        var response = new CreateSubscriptionResponse();

        if (string.IsNullOrEmpty(userId))
        {
            response.Success = false;
            response.Message = "User not authenticated";
            return Results.Unauthorized();
        }

        if (string.IsNullOrEmpty(request.ProductHandle))
        {
            response.Success = false;
            response.Message = "Product handle is required";
            return Results.BadRequest(response);
        }

        try
        {
            var user = await userManager.FindByNameAsync(userId);
            if (user == null)
            {
                response.Success = false;
                response.Message = "User not found";
                return Results.NotFound(response);
            }

            var existingCustomer = await maxioClient.FindCustomerByReferenceAsync(user.Id);
            var customerId = existingCustomer?.Id ?? 0;

            if (customerId == 0)
            {
                var firstName = user.Email?.Split('@')[0] ?? "Customer";
                var lastName = "Account";

                var createCustomerReq = new CreateCustomerRequest
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = user.Email ?? string.Empty,
                    Reference = user.Id
                };

                var customer = await maxioClient.CreateCustomerAsync(createCustomerReq);
                customerId = customer.Id;
            }

            var subscription = await maxioClient.CreateSubscriptionAsync(new Maxio.CreateSubscriptionRequest
            {
                ProductHandle = request.ProductHandle,
                CustomerId = customerId
            });

            response.Success = true;
            response.SubscriptionId = subscription.Id;
            response.State = subscription.State;
            response.ProductName = subscription.ProductName;
            response.Price = subscription.GetPrice();
            response.NextBillingAt = subscription.NextBillingAt;

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Failed to create subscription: {ex.Message}";
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
    public string? Message { get; set; }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime? NextBillingAt { get; set; } = null;
}
