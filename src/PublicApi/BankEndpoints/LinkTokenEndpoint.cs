using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public class CreateLinkTokenRequest : BaseRequest
{
    /// <summary>"bank" (default) to pay by bank, or "brokerage" for the portfolio page.</summary>
    public string? Type { get; set; }
}

public class CreateLinkTokenResponse : BaseResponse
{
    public string LinkToken { get; set; } = string.Empty;
    public DateTimeOffset Expiration { get; set; }
}

/// <summary>
/// Creates what the browser needs to open Plaid Link
/// </summary>
public class LinkTokenEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, ClaimsPrincipal user, ConnectionService connections, CancellationToken cancellationToken) =>
            {
                // The body is optional: a bare POST asks for a bank link token.
                CreateLinkTokenRequest? request = null;
                if (http.Request.ContentLength is > 0 || http.Request.Headers.ContainsKey("Transfer-Encoding"))
                    request = await http.Request.ReadFromJsonAsync<CreateLinkTokenRequest>(cancellationToken);
                return await HandleAsync(request, user, connections, cancellationToken);
            })
            .Produces<CreateLinkTokenResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateLinkTokenRequest? request, ClaimsPrincipal user,
        ConnectionService connections, CancellationToken cancellationToken)
    {
        var kind = ConnectionService.ParseKind(request?.Type) ?? BankConnectionKind.Bank;
        var token = await connections.CreateLinkTokenAsync(user.ShopperId(), kind, cancellationToken);
        return Results.Ok(new CreateLinkTokenResponse { LinkToken = token.LinkToken, Expiration = token.Expiration });
    }
}
