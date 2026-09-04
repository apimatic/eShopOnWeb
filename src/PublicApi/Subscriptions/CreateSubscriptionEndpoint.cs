using System;
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
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionService _service;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager, ISubscriptionService service,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
        _service = service;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, CancellationToken cancellationToken) => await HandleRouteAsync(request, cancellationToken))
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request) => HandleRouteAsync(request, CancellationToken.None);

    private async Task<IResult> HandleRouteAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext!;
        var userName = context.User.Identity?.Name;
        var user = string.IsNullOrWhiteSpace(userName) ? null : await _userManager.FindByNameAsync(userName);
        if (user is null)
            return Results.Unauthorized();

        try
        {
            var subscription = await _service.SubscribeAsync(user, request.ProductHandle, cancellationToken);
            return Results.Ok(new CreateSubscriptionResponse(request.CorrelationId()) { Subscription = subscription });
        }
        catch (SubscriptionPlanNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (MaxioApiException exception)
        {
            _logger.LogError(exception, "Maxio subscription request failed with status {StatusCode}.", exception.StatusCode);
            return Results.Problem("The subscription billing service is unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
