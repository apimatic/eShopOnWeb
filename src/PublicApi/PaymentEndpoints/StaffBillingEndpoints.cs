using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Staff: sets up a monthly invoice for a business customer
/// </summary>
public class CreateInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateInvoiceScheduleRequest request, StaffBillingService billing) =>
            {
                var schedule = await billing.CreateScheduleAsync(request);
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
            (string scheduleId, StaffBillingService billing) => Results.Ok(await billing.PauseAsync(scheduleId)))
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
            (string scheduleId, StaffBillingService billing) => Results.Ok(await billing.ResumeAsync(scheduleId)))
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
            (OnboardSellerRequest request, StaffBillingService billing) =>
            {
                var seller = await billing.OnboardSellerAsync(request);
                return Results.Created($"api/admin/sellers/{seller.SellerId}", seller);
            })
            .Produces<SellerDto>(StatusCodes.Status201Created)
            .WithTags("SellerEndpoints");
    }
}
