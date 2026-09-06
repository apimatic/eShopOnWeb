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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HttpContext _httpContext;

    public CreateSubscriptionEndpoint(
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
        app.MapPost("api/subscriptions", HandleAsync)
            .WithName("CreateSubscription")
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var response = new CreateSubscriptionResponse();

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

        if (string.IsNullOrEmpty(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required");
        }

        var customer = await _subscriptionService.FindOrCreateCustomerAsync(
            user.Email ?? "",
            user.UserName ?? "",
            "",
            userId);

        if (customer == null)
        {
            return Results.StatusCode(500);
        }

        var subscription = await _subscriptionService.CreateSubscriptionAsync(
            customer.Id,
            request.ProductHandle);

        if (subscription == null)
        {
            return Results.StatusCode(500);
        }

        var product = subscription.Product;
        response.Subscription = new SubscriptionDto
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = product?.Name,
            Price = product?.PriceInCents.HasValue ?? false ? product.PriceInCents.Value / 100m : 0m,
            State = subscription.State,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt
        };

        return Results.Created($"api/subscriptions/{subscription.Id}", response);
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; set; }
}
