using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (POST /api/subscriptions).
/// The caller's identity is taken from the JWT and is mapped deterministically to a
/// Maxio customer, so submitting the same request twice never creates two customers or
/// two subscriptions.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal>
{
    private readonly IMaxioBillingService _billingService;
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(IMaxioBillingService billingService, IMapper mapper)
    {
        _billingService = billingService;
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, ct);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user) =>
        HandleAsync(request, user, CancellationToken.None);

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { message = "A productHandle is required." });
        }

        string identity = user.Identity?.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(identity))
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await _billingService.SubscribeAsync(identity, request.ProductHandle, cancellationToken);
            response.Subscription = _mapper.Map<SubscriptionDto>(result.Subscription);
            response.WasCreated = result.WasCreated;

            // 201 Created the first time; 200 OK when the same request is replayed and the
            // existing subscription is returned.
            return result.WasCreated
                ? Results.Created("api/my-subscriptions", response)
                : Results.Ok(response);
        }
        catch (Exception ex) when (MaxioEndpointResult.TryMapError(ex) is { } error)
        {
            return error;
        }
    }
}
