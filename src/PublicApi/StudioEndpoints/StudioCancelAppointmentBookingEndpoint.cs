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

/// <summary>
/// Cancel the caller's own one-to-one session booking
/// </summary>
public class StudioCancelAppointmentBookingEndpoint : IEndpoint<IResult, StudioCancelBookingRequest, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/studio/appointment-bookings/{bookingId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, string bookingId, StudioService service, CancellationToken ct) =>
            {
                return await HandleAsync(new StudioCancelBookingRequest(user.Identity?.Name, bookingId), service, ct);
            })
            .Produces<StudioCancellationDto>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioCancelBookingRequest request, StudioService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public Task<IResult> HandleAsync(StudioCancelBookingRequest request, StudioService service, CancellationToken ct) =>
        StudioResults.RunAsync(() => service.CancelAppointmentBookingAsync(request.UserName, request.BookingId, ct));
}
