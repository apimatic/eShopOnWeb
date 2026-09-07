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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, SubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(
                Summary = "Get user's subscriptions",
                Description = "Returns the authenticated user's active subscriptions",
                OperationId = "subscriptions.getMySubscriptions",
                Tags = new[] { "SubscriptionEndpoints" })]
            async (SubscriptionService service) =>
            {
                return await HandleAsync(new EmptyRequest(), service);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, SubscriptionService service)
    {
        var response = new GetMySubscriptionsResponse();

        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return Results.Unauthorized();
            }

            // Get user ID from JWT claims
            var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)
                ?? httpContext.User.FindFirst("sub");
            if (userIdClaim == null)
            {
                return Results.Unauthorized();
            }

            var userId = userIdClaim.Value;

            // Try to get or create customer (to ensure customer exists)
            var customerId = await service.GetOrCreateMaxioCustomerAsync(userId, null, null, null);

            // Get user's subscriptions
            var subscriptions = await service.ListUserSubscriptionsAsync(customerId);
            response.Subscriptions = subscriptions;
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public IReadOnlyList<SubscriptionDto>? Subscriptions { get; set; }
}
