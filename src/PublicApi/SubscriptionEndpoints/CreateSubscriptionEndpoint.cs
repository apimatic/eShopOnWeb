using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly SubscriptionBillingService _billing;
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(SubscriptionBillingService billing, Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
    {
        _billing = billing;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var context = _httpContextAccessor.HttpContext!;
        return Results.Ok(new CreateSubscriptionResponse
        {
            Subscription = await _billing.SubscribeAsync(context.User, request, context.RequestAborted)
        });
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (CreateSubscriptionRequest request) => HandleAsync(request))
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
