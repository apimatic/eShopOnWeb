using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public class SaveConnectionRequest : BaseRequest
{
    /// <summary>The public token Plaid Link handed the browser.</summary>
    public string? PublicToken { get; set; }

    /// <summary>"bank" or "brokerage"; worked out from the connection when left out.</summary>
    public string? Type { get; set; }

    /// <summary>The account Link's user picked, when it is a bank.</summary>
    public string? AccountId { get; set; }
}

public class SaveConnectionResponse : BaseResponse
{
    public int ConnectionId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? InstitutionName { get; set; }
    public string? AccountName { get; set; }
    public string? AccountMask { get; set; }
}

/// <summary>
/// Saves a connection from what Plaid Link handed the browser
/// </summary>
public class SaveConnectionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SaveConnectionRequest request, ClaimsPrincipal user, ConnectionService connections, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, connections, cancellationToken);
            })
            .Produces<SaveConnectionResponse>(StatusCodes.Status201Created)
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(SaveConnectionRequest request, ClaimsPrincipal user,
        ConnectionService connections, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PublicToken))
            throw BankingException.BadRequest("publicToken is required.");
        var kind = ConnectionService.ParseKind(request.Type);

        var connection = await connections.SaveAsync(user.ShopperId(), request.PublicToken.Trim(), kind, request.AccountId, cancellationToken);
        var response = new SaveConnectionResponse
        {
            ConnectionId = connection.Id,
            Type = connection.Kind == BankConnectionKind.Brokerage ? "brokerage" : "bank",
            InstitutionName = connection.InstitutionName,
            AccountName = connection.AccountName,
            AccountMask = connection.AccountMask,
        };
        return Results.Created($"api/bank/connections/{connection.Id}", response);
    }
}
