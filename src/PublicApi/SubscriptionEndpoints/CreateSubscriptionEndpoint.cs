using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Creates (or returns the existing) subscription for the calling user
/// (POST api/subscriptions).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, UserManager<ApplicationUser>>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        IMaxioSubscriptionService subscriptionService,
        IHttpContextAccessor httpContextAccessor)
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
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, UserManager<ApplicationUser> userManager)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new MaxioSubscriptionException(
                StatusCodes.Status400BadRequest,
                "A planHandle is required.");
        }

        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var user = await SubscriptionEndpointHelpers.RequireUserAsync(userManager, _httpContextAccessor.HttpContext?.User);

        var result = await _subscriptionService.SubscribeAsync(new SubscribeToPlanRequest
        {
            CustomerReference = SubscriptionEndpointHelpers.BuildCustomerReference(user),
            Email = user.Email ?? user.UserName ?? string.Empty,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PlanHandle = request.PlanHandle.Trim()
        }, ct);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription,
            Created = result.Created
        };

        return result.Created
            ? Results.Created((string?)null, response)
            : Results.Ok(response);
    }
}
