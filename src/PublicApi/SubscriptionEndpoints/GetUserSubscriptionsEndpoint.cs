using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest>
{
    private readonly IMaxioSubscriptionService _service;
    private readonly IHttpContextAccessor _contextAccessor;

    public GetUserSubscriptionsEndpoint(IMaxioSubscriptionService service, IHttpContextAccessor contextAccessor)
    {
        _service = service;
        _contextAccessor = contextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync(new EmptyRequest());
            })
            .Produces<GetUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request)
    {
        try
        {
            var context = _contextAccessor.HttpContext;
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
                return Results.Unauthorized();

            var userId = userIdClaim.Value;
            var subscriptions = await _service.GetUserSubscriptionsAsync(userId);

            var response = new GetUserSubscriptionsResponse
            {
                Subscriptions = subscriptions
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class EmptyRequest { }

public class GetUserSubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
