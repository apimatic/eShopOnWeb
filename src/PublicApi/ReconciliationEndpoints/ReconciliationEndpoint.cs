using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly IPaymentService _paymentService;

    public ReconciliationEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (DateTimeOffset? from, DateTimeOffset? to) =>
                {
                    var request = new ReconciliationRequest
                    {
                        From = from ?? DateTimeOffset.UtcNow.AddDays(-30),
                        To = to ?? DateTimeOffset.UtcNow
                    };
                    return await HandleAsync(request);
                })
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        var report = await _paymentService.ReconcileAsync(request.From, request.To);
        return Results.Ok(report);
    }
}
