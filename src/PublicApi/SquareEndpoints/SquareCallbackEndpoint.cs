using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SquareEndpoints;

/// <summary>
/// Where Square sends the merchant's browser back after sign-in. Reached without a token; a callback whose state
/// was not issued by <c>GET /api/square/connect</c> (or was already used, or expired) is refused and changes nothing.
/// </summary>
public class SquareCallbackEndpoint : IEndpoint<IResult, SquareCallbackRequest, SquareOAuthService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/square/callback",
            [AllowAnonymous] async
            ([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, SquareOAuthService oauth, HttpContext context) =>
            {
                return await HandleAsync(new SquareCallbackRequest(code, state, error), oauth, context.RequestAborted);
            })
            .Produces(StatusCodes.Status200OK, contentType: "text/html")
            .Produces(StatusCodes.Status400BadRequest, contentType: "text/html")
            .WithTags("SquareEndpoints");
    }

    public async Task<IResult> HandleAsync(SquareCallbackRequest request, SquareOAuthService oauth, CancellationToken requestAborted)
    {
        using var deadline = SquareTimeouts.Deadline(requestAborted, SquareTimeouts.Request);
        var result = await oauth.CompleteAsync(request.State, request.Code, request.Error, deadline.Token);

        var status = result.Outcome switch
        {
            SquareCallbackOutcome.Connected => StatusCodes.Status200OK,
            SquareCallbackOutcome.Failed => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status400BadRequest,
        };
        var detail = result.Outcome == SquareCallbackOutcome.Connected
            ? $"Connected as {result.BusinessName ?? result.MerchantId}. You can close this window."
            : result.Message;
        var html = $"""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"><title>Square connection</title></head>
            <body><h1>{HtmlEncoder.Default.Encode(result.Outcome == SquareCallbackOutcome.Connected ? "Square connected" : "Square not connected")}</h1>
            <p>{HtmlEncoder.Default.Encode(detail)}</p></body></html>
            """;
        return Results.Content(html, "text/html; charset=utf-8", statusCode: status);
    }
}

public record SquareCallbackRequest(string? Code, string? State, string? Error);
