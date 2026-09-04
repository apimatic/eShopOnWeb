using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IMaxioSubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async ([FromServices] MySubscriptionsEndpoint endpoint) => await endpoint.HandleAsync())
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var context = _httpContextAccessor.HttpContext!;
        var identity = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(identity))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new MySubscriptionsResponse(
            await _service.GetMySubscriptionsAsync(identity, context.RequestAborted)));
    }
}
