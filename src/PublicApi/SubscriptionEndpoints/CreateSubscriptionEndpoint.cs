using System;
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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (HttpContext httpContext, CreateSubscriptionRequest request, IMaxioService maxioService) =>
            {
                return await HandleAsync(request, maxioService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization()
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService)
    {
        throw new NotImplementedException("Use HandleAsync with HttpContext instead");
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService, HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Results.BadRequest("User not found");
        }

        try
        {
            var customer = await maxioService.GetOrCreateCustomerAsync(
                userId,
                user.Email ?? "",
                user.UserName ?? "",
                user.UserName ?? "",
                CancellationToken.None
            );

            var subscription = await maxioService.CreateSubscriptionAsync(
                customer.Id,
                request.ProductHandle,
                CancellationToken.None
            );

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                SubscriptionId = subscription.Id,
                CustomerId = customer.Id,
                State = subscription.State,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = "";

    public string CorrelationId() => Guid.NewGuid().ToString();
}

public class CreateSubscriptionResponse
{
    public string CorrelationId { get; set; }

    public CreateSubscriptionResponse(string correlationId)
    {
        CorrelationId = correlationId;
    }

    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = "";
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
}
