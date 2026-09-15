using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly SubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(SubscriptionService service, IHttpContextAccessor httpContextAccessor, ILogger<CreateSubscriptionEndpoint> logger)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", (CreateSubscriptionRequest request) => HandleAsync(request))
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext!;
            var subscription = await _service.SubscribeAsync(httpContext.User, request.PlanHandle, httpContext.RequestAborted);
            return Results.Created("api/my-subscriptions", subscription);
        }
        catch (SubscriptionValidationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (SubscriptionUnauthorizedException)
        {
            return Results.Unauthorized();
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio subscription creation failed.");
            return Results.Problem("The billing provider could not create the subscription.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
