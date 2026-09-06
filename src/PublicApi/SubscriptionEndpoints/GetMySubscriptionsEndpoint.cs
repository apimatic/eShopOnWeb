using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HttpContext _httpContext;

    public GetMySubscriptionsEndpoint(
        IMaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
        _httpContext = httpContextAccessor.HttpContext!;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleAsync)
            .WithName("GetMySubscriptions")
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new GetMySubscriptionsResponse();

        var userId = _httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Results.NotFound("User not found");
        }

        var customer = await _subscriptionService.FindOrCreateCustomerAsync(
            user.Email ?? "",
            user.UserName ?? "",
            "",
            userId);

        if (customer == null)
        {
            return Results.Ok(response);
        }

        var subscriptions = await _subscriptionService.GetCustomerSubscriptionsAsync(customer.Id);

        response.Subscriptions = subscriptions
            .Select(s => new SubscriptionDto
            {
                Id = s.Id,
                ProductHandle = s.ProductHandle,
                ProductName = s.Product?.Name,
                Price = s.Product?.PriceInCents.HasValue ?? false ? s.Product.PriceInCents.Value / 100m : 0m,
                State = s.State,
                CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                CreatedAt = s.CreatedAt
            })
            .ToList();

        return Results.Ok(response);
    }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
