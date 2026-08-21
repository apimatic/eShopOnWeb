using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionApplicationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlanListEndpoint(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionApplicationService service) => await HandleAsync(service))
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionBilling");
    }

    public async Task<IResult> HandleAsync(ISubscriptionApplicationService service) =>
        Results.Ok(new SubscriptionPlansResponse(
            await service.ListPlansAsync(_httpContextAccessor.HttpContext?.RequestAborted ?? default)));
}

public sealed class CreateSubscriptionEndpoint :
    IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionApplicationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
                ISubscriptionApplicationService service) =>
                await HandleAsync(request, service))
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionBilling");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionApplicationService service)
    {
        var subscription = await service.SubscribeAsync(
            request.ProductHandle,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);
        return Results.Created("api/my-subscriptions", subscription);
    }
}

public sealed class MySubscriptionListEndpoint : IEndpoint<IResult, ISubscriptionApplicationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionListEndpoint(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionApplicationService service) => await HandleAsync(service))
            .Produces<SubscriptionsResponse>()
            .WithTags("SubscriptionBilling");
    }

    public async Task<IResult> HandleAsync(ISubscriptionApplicationService service) =>
        Results.Ok(new SubscriptionsResponse(
            await service.ListMineAsync(_httpContextAccessor.HttpContext?.RequestAborted ?? default)));
}
