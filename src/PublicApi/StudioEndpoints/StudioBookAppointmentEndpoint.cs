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

public record StudioBookAppointmentRequest(string? UserName, AppointmentBookingRequest Body);

/// <summary>
/// Book a one-to-one session
/// </summary>
public class StudioBookAppointmentEndpoint : IEndpoint<IResult, StudioBookAppointmentRequest, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/appointment-bookings",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, AppointmentBookingRequest body, StudioService service, CancellationToken ct) =>
            {
                return await HandleAsync(new StudioBookAppointmentRequest(user.Identity?.Name, body), service, ct);
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioBookAppointmentRequest request, StudioService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public Task<IResult> HandleAsync(StudioBookAppointmentRequest request, StudioService service, CancellationToken ct) =>
        StudioResults.RunAsync(() => service.BookAppointmentAsync(request.UserName, request.Body, ct));
}
