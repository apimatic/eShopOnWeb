using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, IMaxioSubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionCreateEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (ClaimsPrincipal user, SubscriptionCreateRequest request, IMaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .RequireAuthorization()
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithSummary("Create a new subscription");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, IMaxioSubscriptionService subscriptionService)
    {
        try
        {
            if (string.IsNullOrEmpty(request.UserId))
            {
                return Results.BadRequest(new { message = "User ID is required" });
            }

            if (string.IsNullOrEmpty(request.ProductHandle))
            {
                return Results.BadRequest(new { message = "Product handle is required" });
            }

            // Ensure customer exists in Maxio (idempotent)
            var customerId = await subscriptionService.EnsureCustomerExistsAsync(
                userId: request.UserId,
                email: request.Email,
                firstName: request.FirstName,
                lastName: request.LastName);

            if (customerId is null or 0)
            {
                return Results.StatusCode(500);
            }

            // Create subscription
            var subscription = await subscriptionService.CreateSubscriptionAsync(customerId.Value, request.ProductHandle);

            if (subscription == null)
            {
                return Results.StatusCode(500);
            }

            var response = new SubscriptionCreateResponse(request.CorrelationId())
            {
                SubscriptionId = subscription.Id,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                ProductName = subscription.ProductName,
                PriceInCents = subscription.ProductPriceInCents,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                CreatedAt = subscription.CreatedAt
            };

            return Results.Created($"/api/my-subscriptions/{subscription.Id}", response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }
}

public class SubscriptionCreateRequest : BaseRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionCreateResponse()
    {
    }

    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public decimal? PriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
