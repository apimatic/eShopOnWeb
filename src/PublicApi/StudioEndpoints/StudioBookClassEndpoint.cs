using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

public record StudioBookClassRequest(string? UserName, ClassBookingRequest Body);

/// <summary>
/// Book a place in a class
/// </summary>
public class StudioBookClassEndpoint : IEndpoint<IResult, StudioBookClassRequest, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/class-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ClassBookingRequest body, StudioService service, CancellationToken ct) =>
            {
                return await HandleAsync(new StudioBookClassRequest(user.Identity?.Name, body), service, ct);
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioBookClassRequest request, StudioService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public Task<IResult> HandleAsync(StudioBookClassRequest request, StudioService service, CancellationToken ct) =>
        StudioResults.RunAsync(() => service.BookClassAsync(request.UserName, request.Body, ct));
}
