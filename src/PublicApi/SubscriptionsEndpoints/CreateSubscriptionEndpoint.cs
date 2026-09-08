using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

/// <summary>
/// Subscribes the signed-in user to a billing plan (idempotent)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, UserManager<ApplicationUser>>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService, IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(request, userManager);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionsEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, UserManager<ApplicationUser> userManager)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "A product handle is required."
            });
        }

        var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email;
        var (firstName, lastName) = ProfileNames.FromEmail(email);
        var profile = new MaxioCustomerProfile
        {
            UserName = userName,
            Email = email,
            FirstName = firstName,
            LastName = lastName
        };

        var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var result = await _subscriptionService.SubscribeAsync(profile, request.ProductHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            AlreadySubscribed = result.WasAlreadySubscribed,
            Subscription = result.Subscription
        };

        return result.WasAlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
