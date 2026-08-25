using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated user to a plan (idempotent)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
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
            async (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billingService) =>
            {
                var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? httpContext.User.Identity?.Name;
                if (username is null)
                {
                    return Results.Unauthorized();
                }

                var user = await _userManager.FindByNameAsync(username);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                request.UserId = user.Id;
                request.Email = user.Email ?? username;

                return await HandleAsync(request, billingService);
            })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("PlanHandle is required.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var subscription = await billingService.SubscribeAsync(
                request.UserId!, request.Email!, null, null, request.PlanHandle);
            response.Subscription = _mapper.Map<SubscriptionDto>(subscription);
            return Results.Ok(response);
        }
        catch (BillingServiceException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
        }
    }
}
