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

/// <summary>Whether the shop is connected to Square, and as which merchant.</summary>
public class SquareConnectionEndpoint : IEndpoint<IResult, SquareMerchantContextProvider, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/square/connection",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SquareMerchantContextProvider merchants, HttpContext context) =>
            {
                return await HandleAsync(merchants, context.RequestAborted);
            })
            .Produces<SquareConnectionResponse>()
            .WithTags("SquareEndpoints");
    }

    public async Task<IResult> HandleAsync(SquareMerchantContextProvider merchants, CancellationToken requestAborted)
    {
        using var deadline = SquareTimeouts.Deadline(requestAborted, SquareTimeouts.Request);
        try
        {
            var merchant = await merchants.GetAsync(deadline.Token, refresh: true);
            return Results.Ok(new SquareConnectionResponse
            {
                Connected = true,
                ConnectionType = merchant.Source == SquareCredentialSource.OAuthConnection ? "oauth" : "accessToken",
                MerchantId = merchant.MerchantId,
                BusinessName = merchant.BusinessName,
                LocationId = merchant.LocationId,
                LocationName = merchant.LocationName,
            });
        }
        catch (SquareIntegrationException ex) when (ex.Kind is SquareFailureKind.NotConnected or SquareFailureKind.AuthorizationFailed)
        {
            return Results.Ok(new SquareConnectionResponse { Connected = false, Error = ex.Message });
        }
    }
}

public class SquareConnectionResponse : BaseResponse
{
    public bool Connected { get; set; }
    /// <summary><c>oauth</c> (merchant signed in) or <c>accessToken</c> (configured token).</summary>
    public string? ConnectionType { get; set; }
    public string? MerchantId { get; set; }
    public string? BusinessName { get; set; }
    public string? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? Error { get; set; }
}
