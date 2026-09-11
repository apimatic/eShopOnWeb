using System;
using System.Security.Claims;
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

public class SubscriptionCreateRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse() { }
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId) { }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string ProductName { get; set; } = string.Empty;
}

public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, Maxio.IMaxioService, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionCreateEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscriptionCreateRequest request, Maxio.IMaxioService maxioService, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(request, maxioService, userManager);
            })
        .Produces<SubscriptionCreateResponse>()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscriptionCreateRequest request,
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

        var result = await maxioService.CreateSubscriptionAsync(maxioCustomerId, request.ProductHandle);

        var response = new SubscriptionCreateResponse(request.CorrelationId())
        {
            SubscriptionId = result.SubscriptionId,
            State = result.State,
            Price = result.Price,
            NextBillingAt = result.NextBillingAt,
            ActivatedAt = result.ActivatedAt,
            ProductName = result.ProductName
        };

        return Results.Created($"/api/my-subscriptions/{result.SubscriptionId}", response);
    }
}
