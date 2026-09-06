using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioService maxioService, UserManager<ApplicationUser> userManager) =>
            {
                var endpoint = new CreateSubscriptionEndpoint();
                return await endpoint.HandleAsync(request, httpContext, maxioService, userManager);
            })
            .RequireAuthorization()
            .WithName("CreateSubscription")
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, IMaxioService maxioService, UserManager<ApplicationUser> userManager)
    {
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            // Get or create customer in Maxio
            var maxioCustomer = await maxioService.GetOrCreateCustomerAsync(
                userId,
                user.UserName?.Split("@")[0] ?? "User",
                "Customer",
                user.Email ?? "",
                default
            );

            // Create subscription
            var subscription = await maxioService.CreateSubscriptionAsync(
                maxioCustomer.Id,
                request.ProductHandle ?? "eshop-pro",
                default
            );

            var response = new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id,
                CustomerId = subscription.CustomerId,
                State = subscription.State,
                NextBillingAt = subscription.NextBillingAt,
                Message = $"Subscription created successfully. Next billing date: {subscription.NextBillingAt:yyyy-MM-dd}"
            };

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Http.HttpRequestException)
        {
            return Results.BadRequest(new ErrorResponse { Error = "Failed to create subscription. Please check product handle and try again." });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest
{
    [SwaggerParameter(Description = "Product handle (e.g., 'eshop-pro', 'eshop-basic')")]
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string? State { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public string? Message { get; set; }
}

public class ErrorResponse
{
    public string? Error { get; set; }
}
