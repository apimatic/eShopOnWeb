using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, ISubscriptionService>
{
    private readonly ILogger<SubscriptionCreateEndpoint> _logger;

    public SubscriptionCreateEndpoint(ILogger<SubscriptionCreateEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                async (SubscriptionCreateRequest request, ISubscriptionService subscriptionService,
                    System.Security.Claims.ClaimsPrincipal user, CancellationToken cancellationToken) =>
                {
                    request.AuthenticatedUsername = user.Identity?.Name;
                    return await HandleAsync(request, subscriptionService, cancellationToken);
                })
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }

    public Task<IResult> HandleAsync(SubscriptionCreateRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AuthenticatedUsername))
        {
            return Results.Unauthorized();
        }

        var response = new SubscriptionCreateResponse(request.CorrelationId());

        var result = await subscriptionService.SubscribeAsync(request.AuthenticatedUsername, request.PlanHandle, cancellationToken);
        switch (result.Status)
        {
            case ResultStatus.Ok:
                response.Subscription = result.Value.Subscription;
                response.Created = result.Value.Created;
                return result.Value.Created ? Results.Created("api/my-subscriptions", response) : Results.Ok(response);
            case ResultStatus.NotFound:
                return Results.NotFound(new { errors = result.Errors });
            case ResultStatus.Invalid:
                return Results.BadRequest(new { errors = result.Errors });
            default:
                _logger.LogError("Subscription creation failed for {Username} / {PlanHandle}: {Errors}",
                    request.AuthenticatedUsername, request.PlanHandle, string.Join("; ", result.Errors));
                return Results.Problem(title: "Failed to create the subscription.",
                    detail: string.Join("; ", result.Errors), statusCode: 502);
        }
    }
}
