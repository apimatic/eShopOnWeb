using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioSubscriptionService>
{
    private readonly IAppLogger<MySubscriptionsEndpoint> _logger;

    public MySubscriptionsEndpoint(IAppLogger<MySubscriptionsEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (EmptyRequest request, IMaxioSubscriptionService service, HttpContext context) =>
            {
                return await HandleAsync(request, service, context);
            })
            .WithName("GetMySubscriptions")
            .RequireAuthorization()
            .Produces<MySubscriptionsResponse>();
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, IMaxioSubscriptionService service)
    {
        throw new NotImplementedException("Use the context overload");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, IMaxioSubscriptionService service, HttpContext context)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Subscriptions list attempt without authenticated user");
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await service.GetUserSubscriptionsAsync(userId, CancellationToken.None);

            response.Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
            {
                Id = s.Id,
                Handle = s.Handle,
                State = s.State,
                ProductName = s.ProductName,
                PriceInDollars = s.PriceInCents.HasValue ? s.PriceInCents.Value / 100m : null,
                NextBillingDate = s.NextBillingDate,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
            }).ToList();

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to retrieve subscriptions: {ex.Message}");
            response.Message = "Failed to retrieve subscriptions";
            return Results.StatusCode(500);
        }
    }
}

public class UserSubscriptionDto
{
    public int? Id { get; set; }
    public string? Handle { get; set; }
    public string? State { get; set; }
    public string? ProductName { get; set; }
    public decimal? PriceInDollars { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
    public string? Message { get; set; }
}
