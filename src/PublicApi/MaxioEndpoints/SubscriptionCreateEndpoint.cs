using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.MaxioEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateEndpoint.CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionCreateEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, IMaxioSubscriptionService maxioService) =>
            {
                return await HandleAsync(request, maxioService);
            })
           .Produces<SubscriptionCreateResponse>()
           .Produces(StatusCodes.Status401Unauthorized)
           .WithTags("MaxioEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService maxioService)
    {
        var userReference = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userReference))
            return Results.Unauthorized();

        var subscription = await maxioService.SubscribeAsync(userReference, request.ProductHandle);
        var response = new SubscriptionCreateResponse { Subscription = subscription };
        return Results.Ok(response);
    }

    public class CreateSubscriptionRequest
    {
        public string ProductHandle { get; set; } = string.Empty;
    }

    public class SubscriptionCreateResponse
    {
        public SubscriptionDto Subscription { get; set; } = new();
    }
}
