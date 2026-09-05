using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, ListUserSubscriptionsRequest, ListSubscriptionsDependency>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext, IMaxioBillingService billingService,
                   UserManager<ApplicationUser> userManager, CatalogContext catalogContext) =>
            {
                var deps = new ListSubscriptionsDependency(billingService, userManager, httpContext, catalogContext);
                var endpoint = new ListUserSubscriptionsEndpoint();
                return await endpoint.HandleAsync(new ListUserSubscriptionsRequest(), deps);
            })
            .Produces<ListUserSubscriptionsResponse>()
            .WithName("ListUserSubscriptions")
            .WithTags("Subscriptions")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(ListUserSubscriptionsRequest request,
        ListSubscriptionsDependency dependency)
    {
        var userId = dependency.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var user = await dependency.UserManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Results.NotFound("User not found");
        }

        var mapping = await Task.Run(() =>
            dependency.CatalogContext.MaxioCustomerMappings
                .FirstOrDefault(m => m.UserId == userId));

        if (mapping == null)
        {
            return Results.Ok(new ListUserSubscriptionsResponse(request.CorrelationId())
            {
                Subscriptions = new List<UserSubscriptionDto>()
            });
        }

        var subscriptions = await dependency.BillingService.ListCustomerSubscriptionsAsync(mapping.MaxioCustomerId);

        var response = new ListUserSubscriptionsResponse(request.CorrelationId())
        {
            Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                ProductName = s.ProductName,
                ProductHandle = s.ProductHandle,
                Price = s.PricePerBillingCycle,
                BillingPeriod = s.BillingPeriod,
                NextBillingAt = s.NextBillingAt
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingPeriod { get; set; } = string.Empty;
    public DateTime? NextBillingAt { get; set; }
}

public class ListUserSubscriptionsResponse : BaseResponse
{
    public ListUserSubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListUserSubscriptionsResponse()
    {
    }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class ListUserSubscriptionsRequest : BaseRequest
{
}

public class ListSubscriptionsDependency
{
    public IMaxioBillingService BillingService { get; }
    public UserManager<ApplicationUser> UserManager { get; }
    public HttpContext HttpContext { get; }
    public CatalogContext CatalogContext { get; }

    public ListSubscriptionsDependency(
        IMaxioBillingService billingService,
        UserManager<ApplicationUser> userManager,
        HttpContext httpContext,
        CatalogContext catalogContext)
    {
        BillingService = billingService;
        UserManager = userManager;
        HttpContext = httpContext;
        CatalogContext = catalogContext;
    }
}
