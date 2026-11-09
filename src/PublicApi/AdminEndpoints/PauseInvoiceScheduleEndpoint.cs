using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminEndpoints;

/// <summary>
/// Pauses a monthly invoice schedule
/// </summary>
public class PauseInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string scheduleId, InvoiceAndSellerService service, CancellationToken ct) =>
            {
                var result = await service.PauseAsync(scheduleId, ct);
                return Results.Ok(result);
            })
            .Produces<InvoiceScheduleDto>()
            .WithTags("AdminEndpoints");
    }
}
