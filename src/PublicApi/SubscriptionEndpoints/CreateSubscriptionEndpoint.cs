using System;
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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, SubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(
                Summary = "Create a new subscription",
                Description = "Subscribe the authenticated user to a billing plan",
                OperationId = "subscriptions.create",
                Tags = new[] { "SubscriptionEndpoints" })]
            async (CreateSubscriptionRequest request, SubscriptionService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, SubscriptionService service)
    {
        var response = new CreateSubscriptionResponse();

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
            var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
            var firstName = httpContext.User.FindFirst(ClaimTypes.GivenName)?.Value;
            var lastName = httpContext.User.FindFirst(ClaimTypes.Surname)?.Value;

            // Get or create Maxio customer
            var customerId = await service.GetOrCreateMaxioCustomerAsync(userId, email, firstName, lastName);

            // Create subscription
            var subscription = await service.CreateSubscriptionAsync(customerId, request.PlanHandle, userId);

            response.Subscription = subscription;
            return Results.Created($"api/subscriptions/{subscription.Id}", response);
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

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = "";
}

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; set; }
}
