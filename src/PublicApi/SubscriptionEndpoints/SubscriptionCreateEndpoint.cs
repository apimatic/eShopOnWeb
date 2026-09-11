using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using MinimalApi.Endpoint;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Identity;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, MaxioAdvancedBillingClient, IMaxioCustomerService, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _http;
    public SubscriptionCreateEndpoint(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest req, MaxioAdvancedBillingClient client, IMaxioCustomerService svc, UserManager<ApplicationUser> um) =>
            {
                return await HandleWithRequestAsync(req, client, svc, um);
            })
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(MaxioAdvancedBillingClient client, IMaxioCustomerService svc, UserManager<ApplicationUser> um)
    {
        return Results.BadRequest(new { error = "Use POST channel with body" });
    }

    public async Task<IResult> HandleWithRequestAsync(CreateSubscriptionRequest req, MaxioAdvancedBillingClient client, IMaxioCustomerService svc, UserManager<ApplicationUser> um)
    {
        try
        {
            var userId = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            var user = await um.FindByIdAsync(userId);
            if (user == null) return Results.NotFound(new { error = "User not found" });

            var customerId = await svc.EnsureCustomerAsync(userId, user.Email ?? $"{userId}@localhost", user.FirstName ?? "First", "");

            // Request specifies either product handle or id
            var productHandle = req.PlanHandle ?? "eshop-pro";

            var subBody = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId
                }
            };

            var result = await client.Subscriptions.CreateSubscription(subBody, ct: default);
            var sub = result.Subscription;

            var response = new CreateSubscriptionResponse
            {
                SubscriptionId = sub?.Id ?? 0,
                ProductHandle = productHandle,
                CustomerId = customerId,
                State = sub?.State ?? "active",
                NextBillingDate = sub?.CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow.AddMonths(1)
            };

            return Results.Ok(response);
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            var msg = ex.Error?.TryGetErrorListResponse1(out var list) == true ? string.Join(", ", list?.Errors?.Select(e => e.Message) ?? Array.Empty<string>()) : (ex.Error?.TryGetRawError(out var raw) == true ? raw?.ReadAsString() : ex.Message);
            return Results.BadRequest(new { error = msg });
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: 500);
        }
    }
}

public class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = "eshop-pro";
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset NextBillingDate { get; set; }
}
