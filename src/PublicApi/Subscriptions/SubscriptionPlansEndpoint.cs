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

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlansEndpoint(IMaxioSubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async ([FromServices] SubscriptionPlansEndpoint endpoint) => await endpoint.HandleAsync())
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        return Results.Ok(new SubscriptionPlansResponse(
            await _service.GetPlansAsync(_httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None)));
    }
}
