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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlanListEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                (MaxioSubscriptionService service) => HandleAsync(service))
            .Produces<SubscriptionPlanDto[]>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService service)
    {
        try
        {
            return Results.Ok(await service.ListPlansAsync(RequestCancellation()));
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The billing provider could not load subscription plans.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Maxio is not configured", StringComparison.Ordinal))
        {
            return Results.Problem("Subscription billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private CancellationToken RequestCancellation() => _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}

public sealed class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioSubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                (CreateSubscriptionRequest request, MaxioSubscriptionService service) => HandleAsync(request, service))
            .Produces<SubscriptionDto>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioSubscriptionService service)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { error = "planHandle is required." });
        }

        var principal = _httpContextAccessor.HttpContext?.User;
        var userName = principal?.Identity?.Name;
        var user = userName is null ? null : await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.SubscribeAsync(user, request.PlanHandle.Trim(), RequestCancellation()));
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The billing provider could not complete the subscription request.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Maxio is not configured", StringComparison.Ordinal))
        {
            return Results.Problem("Subscription billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private CancellationToken RequestCancellation() => _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}

public sealed class MySubscriptionListEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionListEndpoint(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                (MaxioSubscriptionService service) => HandleAsync(service))
            .Produces<SubscriptionDto[]>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService service)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var userName = principal?.Identity?.Name;
        var user = userName is null ? null : await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await service.ListMySubscriptionsAsync(user, RequestCancellation()));
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The billing provider could not load subscriptions.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Maxio is not configured", StringComparison.Ordinal))
        {
            return Results.Problem("Subscription billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private CancellationToken RequestCancellation() => _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
