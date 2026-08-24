using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: ensures a single Maxio
/// customer per shopper and returns the existing subscription when the shopper is
/// already subscribed to the requested plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService, UserManager<ApplicationUser>>
{
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService, UserManager<ApplicationUser> userManager) =>
            {
                request.Username = user.GetUsername() ?? string.Empty;
                return await HandleAsync(request, billingService, userManager);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = "ProductHandle is required." }.ToString());
        }

        var appUser = await userManager.FindByNameAsync(request.Username);
        if (appUser is null)
        {
            return Results.Unauthorized();
        }

        // The username (an email in eShopOnWeb) is the stable Maxio customer reference: it
        // survives re-seeds of the local identity store, so re-subscribing never duplicates customers.
        var email = appUser.Email ?? appUser.UserName!;
        var displayName = email.Split('@')[0];

        try
        {
            var subscription = await billingService.SubscribeAsync(
                customerReference: appUser.UserName!,
                email: email,
                displayName: displayName,
                productHandle: request.ProductHandle);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = _mapper.Map<SubscriptionDto>(subscription)
            };
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return Results.BadRequest(new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = ex.Message }.ToString());
        }
    }
}
