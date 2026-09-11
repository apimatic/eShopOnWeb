using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize(AuthenticationSchemes = "Bearer")]
public class SubscriptionMyListEndpoint
{
    private readonly ILogger<SubscriptionMyListEndpoint> _logger;

    public SubscriptionMyListEndpoint(ILogger<SubscriptionMyListEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (SubscriptionService svc, UserManager<ApplicationUser> userManager, HttpContext httpContext) =>
        {
            return await HandleAsync(svc, userManager, httpContext);
        })
        .Produces<System.Collections.Generic.List<SubscriptionResponseDto>>()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionService service, UserManager<ApplicationUser> userManager, HttpContext httpContext)
    {
        try
        {
            var username = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? httpContext.User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                return Results.Unauthorized();

            var user = await userManager.FindByNameAsync(username);
            if (user == null)
                return Results.Unauthorized();

            var email = user.Email ?? username;
            var client = service.Client;

            var list = await client.Customers.ListCustomers(direction: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, q: email, page: 1, perPage: 50, ct: CancellationToken.None);
            var existing = list.FirstOrDefault(c => c.Customer?.Email == email || c.Customer?.Reference == user.Id);
            if (existing?.Customer == null)
                return Results.Ok(new System.Collections.Generic.List<SubscriptionResponseDto>());

            var customerId = existing.Customer.Id ?? 0;
            if (customerId == 0)
                return Results.Ok(new System.Collections.Generic.List<SubscriptionResponseDto>());

            var subs = await client.Customers.ListCustomerSubscriptions(customerId, ct: CancellationToken.None);
            var result = new System.Collections.Generic.List<SubscriptionResponseDto>();
            foreach (var s in subs)
            {
                var sub = s.Subscription;
                if (sub == null) continue;
                result.Add(new SubscriptionResponseDto
                {
                    Id = sub.Id,
                    State = sub.State?.ToString() ?? "Unknown",
                    PlanHandle = sub.Product?.Handle ?? "",
                    Price = 299m,
                    NextBillingAt = sub.NextAssessmentAt,
                    CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                    CustomerEmail = email
                });
            }
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing my subscriptions");
            return Results.StatusCode(500);
        }
    }
}
