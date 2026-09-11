using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionListMyRequest : BaseRequest { }

public class SubscriptionListMyResponse : BaseResponse
{
    public SubscriptionListMyResponse() { }
    public SubscriptionListMyResponse(Guid correlationId) : base(correlationId) { }
    public List<SubscriptionDetailDto> Subscriptions { get; set; } = new();
}

public class SubscriptionDetailDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscriptionListMyEndpoint : IEndpoint<IResult, SubscriptionListMyRequest, Maxio.IMaxioService, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionListMyEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (Maxio.IMaxioService maxioService, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(new SubscriptionListMyRequest(), maxioService, userManager);
            })
        .Produces<SubscriptionListMyResponse>()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscriptionListMyRequest request,
        Maxio.IMaxioService maxioService,
        UserManager<ApplicationUser> userManager)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User == null)
        {
            return Results.Unauthorized();
        }

        var user = await userManager.GetUserAsync(httpContext.User);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var maxioCustomerId = await maxioService.EnsureCustomerExistsAsync(
            user.Id,
            user.Email ?? string.Empty,
            user.UserName ?? "User",
            string.Empty);

        var subs = await maxioService.ListMySubscriptionsAsync(maxioCustomerId);

        var response = new SubscriptionListMyResponse(request.CorrelationId());
        response.Subscriptions.AddRange(subs.Select(s => new SubscriptionDetailDto
        {
            SubscriptionId = s.SubscriptionId,
            State = s.State,
            Price = s.Price,
            NextBillingAt = s.NextBillingAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            ActivatedAt = s.ActivatedAt,
            ProductName = s.ProductName,
            ProductHandle = s.ProductHandle
        }));

        return Results.Ok(response);
    }
}
