using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SquareEndpoints;

public class SquareConnectRequest : BaseRequest
{
    public SquareConnectRequest(string? startedBy, CancellationToken cancellationToken)
    {
        StartedBy = startedBy;
        CancellationToken = cancellationToken;
    }

    public string? StartedBy { get; }
    public CancellationToken CancellationToken { get; }
}

public class SquareConnectResponse : BaseResponse
{
    public SquareConnectResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SquareConnectResponse()
    {
    }

    /// <summary>The Square page the merchant opens in a browser to sign in and approve the shop's access.</summary>
    public string SignInUrl { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Starts connecting the merchant's Square account (operator action).
/// </summary>
public class SquareConnectEndpoint : IEndpoint<IResult, SquareConnectRequest, SquareOAuthService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/square/connect",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, SquareOAuthService oauthService) =>
            {
                using var deadline = SquareRequestDeadline.Start(httpContext, SquareConstants.RequestBudget);
                return await HandleAsync(new SquareConnectRequest(httpContext.User.Identity?.Name, deadline.Token), oauthService);
            })
            .Produces<SquareConnectResponse>()
            .WithTags("SquareEndpoints");
    }

    public async Task<IResult> HandleAsync(SquareConnectRequest request, SquareOAuthService oauthService)
    {
        var start = await oauthService.StartAsync(request.StartedBy, request.CancellationToken);
        return Results.Ok(new SquareConnectResponse(request.CorrelationId())
        {
            SignInUrl = start.SignInUrl,
            ExpiresAt = start.ExpiresAt,
        });
    }
}
