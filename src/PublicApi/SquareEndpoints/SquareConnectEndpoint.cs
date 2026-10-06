using System;
using System.Security.Claims;
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

/// <summary>
/// Starts connecting the merchant's Square account: returns the Square page the merchant opens to sign in and
/// approve the shop's access.
/// </summary>
public class SquareConnectEndpoint : IEndpoint<IResult, ClaimsPrincipal, SquareOAuthService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/square/connect",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, SquareOAuthService oauth, HttpContext context) =>
            {
                return await HandleAsync(user, oauth, context.RequestAborted);
            })
            .Produces<SquareConnectResponse>()
            .WithTags("SquareEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, SquareOAuthService oauth, CancellationToken requestAborted)
    {
        using var deadline = SquareTimeouts.Deadline(requestAborted, SquareTimeouts.Request);
        var signIn = await oauth.BeginAsync(user.Identity?.Name ?? "unknown", deadline.Token);
        return Results.Ok(new SquareConnectResponse
        {
            SignInUrl = signIn.SignInUrl,
            ExpiresAt = signIn.ExpiresAt,
        });
    }
}

public class SquareConnectResponse : BaseResponse
{
    public string SignInUrl { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
