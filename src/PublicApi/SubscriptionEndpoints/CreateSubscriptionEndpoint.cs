using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Linq;

using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class CreateSubscriptionEndpointExtension
{
    public static void MapCreateSubscription(this WebApplication app)
    {
        app.MapPost("api/subscriptions", CreateSubscription)
            .WithName("CreateSubscription")
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> CreateSubscription(
        CreateSubscriptionRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        MaxioSubscriptionService service)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return Results.NotFound();

        try
        {
            var customerId = await service.GetOrCreateCustomerAsync(
                userId,
                user.Email ?? string.Empty,
                user.FirstName ?? string.Empty,
                user.LastName ?? string.Empty);

            var subscription = await service.CreateSubscriptionAsync(
                userId,
                customerId,
                request.ProductHandle,
                user.Email ?? string.Empty,
                user.FirstName ?? string.Empty,
                user.LastName ?? string.Empty);

            var response = new CreateSubscriptionResponse(Guid.NewGuid())
            {
                Id = subscription.Id,
                Status = subscription.Status,
                NextBillingDate = subscription.NextBillingDate,
                StartDate = subscription.StartDate
            };

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? NextBillingDate { get; set; }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }
}
