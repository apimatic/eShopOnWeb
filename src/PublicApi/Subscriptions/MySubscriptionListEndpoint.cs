using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionListEndpoint : IEndpoint<IResult, EmptySubscriptionRequest>
{
    private readonly SubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionListEndpoint(SubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", () => HandleAsync(new EmptySubscriptionRequest()))
            .Produces<MySubscriptionsResponse>()
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptySubscriptionRequest request)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext!;
            var subscriptions = await _service.ListMySubscriptionsAsync(httpContext.User, httpContext.RequestAborted);
            return Results.Ok(new MySubscriptionsResponse(subscriptions));
        }
        catch (SubscriptionUnauthorizedException)
        {
            return Results.Unauthorized();
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The billing provider could not return your subscriptions.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
