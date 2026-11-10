using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

public record StudioAdminBookClassRequest(AdminClassBookingRequest Body);

/// <summary>
/// Place a shopper in any class in the timetable (administrators only)
/// </summary>
public class StudioAdminBookClassEndpoint : IEndpoint<IResult, StudioAdminBookClassRequest, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/studio/admin/class-bookings",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (AdminClassBookingRequest body, StudioService service, CancellationToken ct) =>
            {
                return await HandleAsync(new StudioAdminBookClassRequest(body), service, ct);
            })
            .Produces<StudioBookingDto>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioAdminBookClassRequest request, StudioService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public Task<IResult> HandleAsync(StudioAdminBookClassRequest request, StudioService service, CancellationToken ct) =>
        StudioResults.RunAsync(() => service.BookClassForShopperAsync(request.Body, ct));
}
