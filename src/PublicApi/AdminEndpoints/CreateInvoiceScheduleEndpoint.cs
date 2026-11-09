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
/// Sets up a monthly invoice for a business customer
/// </summary>
public class CreateInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateInvoiceScheduleRequest request, InvoiceAndSellerService service, CancellationToken ct) =>
            {
                var result = await service.CreateScheduleAsync(request, ct);
                return Results.Created($"api/admin/invoice-schedules/{result.ScheduleId}", result);
            })
            .Produces<InvoiceScheduleDto>()
            .WithTags("AdminEndpoints");
    }
}
