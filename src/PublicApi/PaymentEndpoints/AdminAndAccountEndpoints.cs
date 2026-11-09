using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Closes the account of the shopper in the JWT and erases their data, at Stax as well as in the shop
/// </summary>
public class CloseAccountEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, AccountService service, CancellationToken ct) =>
                Results.Ok(await service.Close(user, ct)))
            .Produces<CloseAccountDto>()
            .WithTags("AccountEndpoints");
    }
}

/// <summary>
/// Staff: sets up a monthly invoice for a business customer
/// </summary>
public class CreateInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateInvoiceScheduleRequest request, InvoiceScheduleService service, CancellationToken ct) =>
            {
                var schedule = await service.Create(request, ct);
                return Results.Created($"api/admin/invoice-schedules/{schedule.ScheduleId}", schedule);
            })
            .Produces<InvoiceScheduleDto>(StatusCodes.Status201Created)
            .WithTags("InvoiceScheduleEndpoints");
    }
}

/// <summary>
/// Staff: pauses a monthly invoice schedule
/// </summary>
public class PauseInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string scheduleId, InvoiceScheduleService service, CancellationToken ct) =>
                Results.Ok(await service.Pause(scheduleId, ct)))
            .Produces<InvoiceScheduleDto>()
            .WithTags("InvoiceScheduleEndpoints");
    }
}

/// <summary>
/// Staff: resumes a paused monthly invoice schedule
/// </summary>
public class ResumeInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string scheduleId, InvoiceScheduleService service, CancellationToken ct) =>
                Results.Ok(await service.Resume(scheduleId, ct)))
            .Produces<InvoiceScheduleDto>()
            .WithTags("InvoiceScheduleEndpoints");
    }
}

/// <summary>
/// Administrator: onboards a seller as a new merchant
/// </summary>
public class OnboardSellerEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/sellers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (OnboardSellerRequest request, SellerService service, CancellationToken ct) =>
            {
                var seller = await service.Onboard(request, ct);
                return Results.Created($"api/admin/sellers/{seller.SellerId}", seller);
            })
            .Produces<SellerDto>(StatusCodes.Status201Created)
            .WithTags("SellerEndpoints");
    }
}
