using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// GET /api/invoices/reconciliation?from={from}&amp;to={to} — operator report lining the
/// provider's own record of bills raised in a date range up against what eShop believes it
/// raised, making plain which bills are eShop's and which are other activity on the shared
/// provider account. from and to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, string, string, IInvoiceAppService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IInvoiceAppService appService) =>
                await HandleAsync(from, to, appService))
            .Produces<ReconciliationReportDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string from, string to, IInvoiceAppService appService)
    {
        var result = await appService.ReconcileAsync(from, to);
        return result.ToHttpResult();
    }
}
