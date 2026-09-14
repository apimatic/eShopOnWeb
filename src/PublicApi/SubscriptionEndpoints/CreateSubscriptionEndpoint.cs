using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, UserManager<ApplicationUser>>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        ISubscriptionBillingService billingService,
        IHttpContextAccessor httpContextAccessor)
    {
        _billingService = billingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, UserManager<ApplicationUser> userManager) =>
                await HandleAsync(request, userManager))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        UserManager<ApplicationUser> userManager)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ProductHandle)] = new[] { "ProductHandle is required." }
            });
        }

        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var user = await SubscriptionEndpointSupport.GetBillingUserAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return await SubscriptionEndpointSupport.ExecuteAsync(async () =>
        {
            var enrollment = await _billingService.SubscribeAsync(
                user,
                request.ProductHandle.Trim(),
                _httpContextAccessor.HttpContext?.RequestAborted ?? default);
            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = SubscriptionDto.From(enrollment.Subscription),
                AlreadyExisted = enrollment.AlreadyExisted
            };

            return enrollment.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created("/api/my-subscriptions", response);
        });
    }
}

