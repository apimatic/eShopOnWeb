using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioApiService, IHttpContextAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioApiService maxioApi, IHttpContextAccessor httpContextAccessor) =>
            {
                return await HandleAsync(maxioApi, httpContextAccessor);
            })
            .RequireAuthorization()
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioApiService maxioApi, IHttpContextAccessor httpContextAccessor)
    {
        var response = new GetMySubscriptionsResponse(Guid.NewGuid());
        response.Subscriptions = new List<MySubscriptionDto>();

        var httpContext = httpContextAccessor.HttpContext;
        var user = httpContext!.User;
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            response.Success = false;
            response.ErrorMessage = "User not authenticated";
            return Results.Unauthorized();
        }

        var customer = await maxioApi.LookupCustomerByReferenceAsync(userId);
        if (customer == null)
        {
            response.Success = true;
            response.Message = "No subscriptions found";
            return Results.Ok(response);
        }

        var subscriptions = await maxioApi.ListCustomerSubscriptionsAsync(customer.Id);
        if (subscriptions?.Subscriptions != null)
        {
            foreach (var sub in subscriptions.Subscriptions)
            {
                response.Subscriptions.Add(new MySubscriptionDto
                {
                    SubscriptionId = sub.Id,
                    State = sub.State,
                    ProductHandle = sub.ProductHandle,
                    NextBillingAt = sub.NextBillingAt,
                    MrrPerMonth = sub.MrrInCents.HasValue ? sub.MrrInCents.Value / 100m : 0,
                    CreatedAt = sub.CreatedAt,
                    UpdatedAt = sub.UpdatedAt
                });
            }
        }

        response.Success = true;
        response.Message = response.Subscriptions.Count > 0
            ? $"Found {response.Subscriptions.Count} subscription(s)"
            : "No active subscriptions";

        return Results.Ok(response);
    }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}

public class MySubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public decimal MrrPerMonth { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
