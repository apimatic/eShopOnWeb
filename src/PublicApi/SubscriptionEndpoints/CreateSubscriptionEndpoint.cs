using System;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMapper _mapper;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaxioBillingService _billingService;

    public CreateSubscriptionEndpoint(IMapper mapper, UserManager<ApplicationUser> userManager, IMaxioBillingService billingService)
    {
        _mapper = mapper;
        _userManager = userManager;
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .WithName("CreateSubscription")
            .Produces<CreateSubscriptionResponse>()
            .WithTags("Subscriptions")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse();

        try
        {
            var userId = httpContext.User.FindFirst("sub")?.Value ?? httpContext.User.Identity?.Name;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            var (success, message, subscription) = await _billingService.CreateSubscriptionAsync(
                userId,
                user.UserName ?? "User",
                user.UserName ?? "User",
                user.Email ?? "",
                request.ProductHandle);

            response.Success = success;
            response.Message = message;

            if (success && subscription != null)
            {
                user.MaxioCustomerId = subscription.MaxioSubscriptionId;
                await _userManager.UpdateAsync(user);

                response.Subscription = _mapper.Map<SubscriptionDto>(subscription);
            }

            var statusCode = success ? StatusCodes.Status201Created : StatusCodes.Status400BadRequest;
            return Results.Json(response, statusCode: statusCode);
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Error: {ex.Message}";
            return Results.Json(response, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
