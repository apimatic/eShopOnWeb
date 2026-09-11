using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize(AuthenticationSchemes = "Bearer")]
public class SubscriptionCreateEndpoint
{
    private readonly ILogger<SubscriptionCreateEndpoint> _logger;

    public SubscriptionCreateEndpoint(ILogger<SubscriptionCreateEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (SubscribeRequest req, SubscriptionService svc, UserManager<ApplicationUser> userManager, HttpContext httpContext) =>
        {
            return await HandleAsync(req, svc, userManager, httpContext);
        })
        .Produces<SubscriptionResponseDto>(200)
        .Produces(401)
        .Produces(500)
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService service, UserManager<ApplicationUser> userManager, HttpContext httpContext)
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
            int customerId = 0;

            // Idempotent customer lookup by email
            var list = await client.Customers.ListCustomers(direction: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, q: email, page: 1, perPage: 50, ct: CancellationToken.None);
            var existing = list.FirstOrDefault(c => c.Customer?.Email == email || c.Customer?.Reference == user.Id);
            if (existing?.Customer != null)
            {
                customerId = existing.Customer.Id ?? 0;
            }
            else
            {
                var createReq = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        Email = email,
                        FirstName = username,
                        LastName = "",
                        Reference = user.Id
                    }
                };
                var created = await client.Customers.CreateCustomer(createReq, ct: CancellationToken.None);
                customerId = created.Customer?.Id ?? 0;
            }

            if (customerId == 0)
                return Results.BadRequest(new { error = "Could not resolve or create Maxio customer" });

            var subReq = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = request.PlanHandle,
                    DeferSignup = false
                }
            };

            var subResp = await client.Subscriptions.CreateSubscription(subReq, ct: CancellationToken.None);
            var sub = subResp.Subscription;

            return Results.Ok(new SubscriptionResponseDto
            {
                Id = sub?.Id,
                State = sub?.State?.ToString() ?? "Unknown",
                PlanHandle = sub?.Product?.Handle ?? request.PlanHandle,
                Price = 299m,
                NextBillingAt = sub?.NextAssessmentAt,
                CurrentPeriodEndsAt = sub?.CurrentPeriodEndsAt,
                CustomerEmail = email
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription creation error");
            return Results.StatusCode(500);
        }
    }
}
