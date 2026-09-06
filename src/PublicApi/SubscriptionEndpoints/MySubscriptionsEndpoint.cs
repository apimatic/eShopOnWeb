using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, MaxioSubscriptionService>
{
    private readonly ILogger<MySubscriptionsEndpoint> _logger;

    public MySubscriptionsEndpoint(ILogger<MySubscriptionsEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (MaxioSubscriptionService maxioService, HttpContext httpContext) =>
            {
                return await HandleAsync(new EmptyRequest(), maxioService, httpContext);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, MaxioSubscriptionService maxioService)
    {
        throw new NotImplementedException("Use the overload with HttpContext");
    }

    private async Task<IResult> HandleAsync(EmptyRequest request, MaxioSubscriptionService maxioService, HttpContext httpContext)
    {
        var response = new MySubscriptionsResponse(Guid.NewGuid());

        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                userId = "anonymous";
            }

            var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ?? userId;
            if (!email.Contains("@"))
            {
                email = userId + "@eshop.local";
            }
            var customer = await maxioService.GetOrCreateCustomerAsync(userId, email);
            var subscriptions = await maxioService.GetCustomerSubscriptionsAsync(customer.Id);

            response.Subscriptions = subscriptions
                .Select(s => new UserSubscriptionDto
                {
                    Id = s.Id,
                    State = s.State,
                    ProductHandle = s.ProductHandle,
                    ProductName = s.ProductName,
                    CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                    NextAssessmentAt = s.NextAssessmentAt,
                    BalanceInCents = s.BalanceInCents,
                    ActivatedAt = s.ActivatedAt,
                    CreatedAt = s.CreatedAt
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving subscriptions");
            response.ErrorMessage = ex.Message;
            return Results.BadRequest(response);
        }

        return Results.Ok(response);
    }
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public DateTime? CurrentPeriodStartedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public long BalanceInCents { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new List<UserSubscriptionDto>();
    public string? ErrorMessage { get; set; }
}
