using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioApiClient, MaxioOptions>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, IMaxioApiClient maxioClient, MaxioOptions options) =>
            {
                return await HandleAsync(request, maxioClient, options);
            })
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        IMaxioApiClient maxioClient,
        MaxioOptions options)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var user = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP context available.");

        var email = user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("email")
            ?? user.Identity?.Name
            ?? throw new InvalidOperationException("Unable to determine user identity from JWT.");

        var firstName = user.FindFirstValue(ClaimTypes.GivenName) ?? "eShop";
        var lastName = user.FindFirstValue(ClaimTypes.Surname) ?? "User";

        var customerReference = email;

        var existingCustomerId = await maxioClient.FindCustomerByReferenceAsync(customerReference);
        int customerId;

        if (existingCustomerId.HasValue)
        {
            customerId = existingCustomerId.Value;
        }
        else
        {
            customerId = await maxioClient.CreateCustomerAsync(customerReference, firstName, lastName, email);
        }

        var subscription = await maxioClient.CreateSubscriptionAsync(customerId, request.ProductHandle);
        response.Subscription = subscription;

        return Results.Ok(response);
    }
}
