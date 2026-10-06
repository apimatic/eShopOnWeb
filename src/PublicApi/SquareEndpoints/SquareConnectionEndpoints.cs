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

public class SquareConnectionResponse : BaseResponse
{
    public SquareConnectionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SquareConnectionResponse()
    {
    }

    public bool Connected { get; set; }

    /// <summary>"sign-in" (merchant connected through Square sign-in) or "configured-access-token".</summary>
    public string? ConnectedVia { get; set; }
    public string? MerchantId { get; set; }
    public string? BusinessName { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string? Problem { get; set; }

    public static SquareConnectionResponse From(Guid correlationId, SquareConnectionStatus status) => new(correlationId)
    {
        Connected = status.Connected,
        ConnectedVia = status.ConnectedVia,
        MerchantId = status.MerchantId,
        BusinessName = status.BusinessName,
        Environment = status.Environment,
        Problem = status.Problem,
    };
}

public class SquareCallbackRequest : BaseRequest
{
    public SquareCallbackRequest(string? code, string? state, string? error, string? errorDescription, CancellationToken cancellationToken)
    {
        Code = code;
        State = state;
        Error = error;
        ErrorDescription = errorDescription;
        CancellationToken = cancellationToken;
    }

    public string? Code { get; }
    public string? State { get; }
    public string? Error { get; }
    public string? ErrorDescription { get; }
    public CancellationToken CancellationToken { get; }
}

/// <summary>
/// Where Square sends the merchant's browser after they approve the shop. Reached without a token;
/// the sign-in state proves the shop started it.
/// </summary>
public class SquareCallbackEndpoint : IEndpoint<IResult, SquareCallbackRequest, SquareOAuthService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/square/callback",
            [AllowAnonymous] async (HttpContext httpContext, SquareOAuthService oauthService) =>
            {
                var query = httpContext.Request.Query;
                using var deadline = SquareRequestDeadline.Start(httpContext, SquareConstants.RequestBudget);
                return await HandleAsync(new SquareCallbackRequest(
                    query["code"], query["state"], query["error"], query["error_description"], deadline.Token), oauthService);
            })
            .Produces<SquareConnectionResponse>()
            .WithTags("SquareEndpoints");
    }

    public async Task<IResult> HandleAsync(SquareCallbackRequest request, SquareOAuthService oauthService)
    {
        var status = await oauthService.CompleteAsync(request.State, request.Code, request.Error, request.ErrorDescription, request.CancellationToken);
        return Results.Ok(SquareConnectionResponse.From(request.CorrelationId(), status));
    }
}

public class SquareConnectionRequest : BaseRequest
{
    public SquareConnectionRequest(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
    }

    public CancellationToken CancellationToken { get; }
}

/// <summary>
/// Whether the shop is connected to Square, and as which merchant (operator action).
/// </summary>
public class SquareConnectionEndpoint : IEndpoint<IResult, SquareConnectionRequest, SquareOAuthService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/square/connection",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, SquareOAuthService oauthService) =>
            {
                using var deadline = SquareRequestDeadline.Start(httpContext, SquareConstants.RequestBudget);
                return await HandleAsync(new SquareConnectionRequest(deadline.Token), oauthService);
            })
            .Produces<SquareConnectionResponse>()
            .WithTags("SquareEndpoints");
    }

    public async Task<IResult> HandleAsync(SquareConnectionRequest request, SquareOAuthService oauthService)
    {
        var status = await oauthService.GetStatusAsync(request.CancellationToken);
        return Results.Ok(SquareConnectionResponse.From(request.CorrelationId(), status));
    }
}
