using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, IMapper mapper)
    {
        _userManager = userManager;
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequestPayload payload, ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    return Results.NotFound("User not found");
                }

                var nameParts = (user.UserName ?? "Customer User").Split(' ');
                var firstName = nameParts.Length > 0 ? nameParts[0] : "Customer";
                var lastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : "User";

                var request = new CreateSubscriptionRequest
                {
                    UserId = userId,
                    FirstName = firstName,
                    LastName = lastName,
                    Email = user.Email ?? string.Empty,
                    PlanHandle = payload.PlanHandle
                };

                return await HandleAsync(request, subscriptionService);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        try
        {
            if (string.IsNullOrEmpty(request.PlanHandle))
            {
                return Results.BadRequest(new { error = "Plan handle is required" });
            }

            var subscription = await subscriptionService.CreateSubscriptionAsync(
                request.UserId,
                request.FirstName,
                request.LastName,
                request.Email,
                request.PlanHandle);

            if (subscription == null)
            {
                return Results.BadRequest(new { error = "Failed to create subscription" });
            }

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                SubscriptionId = subscription.Id,
                State = subscription.State,
                ProductHandle = subscription.ProductHandle,
                NextBillingAt = subscription.NextBillingAt,
                Status = "success"
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public class CreateSubscriptionRequestPayload : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string UserId { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public string Status { get; set; } = string.Empty;
}
