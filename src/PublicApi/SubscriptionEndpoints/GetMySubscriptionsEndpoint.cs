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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, IMaxioService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public GetMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, IMaxioService maxioService) =>
            {
                return await HandleAsyncInternal(new GetMySubscriptionsRequest(), maxioService, httpContext);
            })
            .Produces<GetMySubscriptionsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization()
            .WithTags("Subscriptions");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioService maxioService)
    {
        throw new NotImplementedException("Use HandleAsyncInternal with HttpContext instead");
    }

    private async Task<IResult> HandleAsyncInternal(GetMySubscriptionsRequest request, IMaxioService maxioService, HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var response = new GetMySubscriptionsResponse(request.CorrelationId());

        try
        {
            var customer = await maxioService.GetOrCreateCustomerAsync(
                userId,
                user.Email ?? "",
                user.UserName ?? "",
                user.UserName ?? "",
                CancellationToken.None
            );

            var subscriptions = await maxioService.GetSubscriptionsAsync(customer.Id, CancellationToken.None);

            response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                ActivatedAt = s.ActivatedAt,
                CreatedAt = s.CreatedAt,
                Product = s.Product != null ? new SubscriptionPlanDto
                {
                    Id = s.Product.Id,
                    Name = s.Product.Name,
                    Handle = s.Product.Handle,
                    Description = s.Product.Description,
                    Price = s.Product.PriceInCents / 100m,
                    Interval = s.Product.Interval,
                    IntervalUnit = s.Product.IntervalUnit
                } : null
            }).ToList();

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class GetMySubscriptionsRequest
{
    public string CorrelationId() => Guid.NewGuid().ToString();
}

public class GetMySubscriptionsResponse
{
    public string CorrelationId { get; set; }

    public GetMySubscriptionsResponse(string correlationId)
    {
        CorrelationId = correlationId;
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public SubscriptionPlanDto? Product { get; set; }
}
