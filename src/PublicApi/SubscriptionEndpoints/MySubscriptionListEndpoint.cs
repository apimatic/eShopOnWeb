using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List subscriptions for the authenticated user
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, IMaxioClient>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionListEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (IMaxioClient maxioClient) =>
        {
            return await HandleAsync(maxioClient);
        })
        .RequireAuthorization()
        .Produces<MySubscriptionListResponse>()
        .Produces(401)
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioClient maxioClient)
    {
        var response = new MySubscriptionListResponse();

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Results.Json(response, statusCode: 500);
        }

        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub")
            ?? httpContext.User.Identity?.Name;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Json(response, statusCode: 401);
        }

        var reference = $"eshop:{userId}";
        var customer = await maxioClient.FindCustomerByReferenceAsync(reference);

        if (customer == null)
        {
            return Results.Ok(response);
        }

        var subscriptions = await maxioClient.GetSubscriptionsByCustomerIdAsync(customer.Id);

        response.Subscriptions = subscriptions.Select(sub => new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            PlanName = sub.Product?.Name ?? "Unknown",
            PlanHandle = sub.Product?.Handle,
            Price = (sub.Product?.PriceInCents ?? 0) / 100m,
            CurrentPeriodEndsAt = ParseDateTime(sub.CurrentPeriodEndsAt),
            NextAssessmentAt = ParseDateTime(sub.NextAssessmentAt),
            ActivatedAt = ParseDateTime(sub.ActivatedAt),
            CreatedAt = ParseDateTime(sub.CreatedAt)
        }).ToList();

        return Results.Ok(response);
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, out var dt)) return dt;
        return null;
    }
}
