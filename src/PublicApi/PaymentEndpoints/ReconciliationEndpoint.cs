using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, CancellationToken>
{
    private readonly PaymentService _paymentService;

    public ReconciliationEndpoint(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken) =>
                {
                    if (from is null || to is null)
                    {
                        throw new PaymentValidationException(
                            "Both 'from' and 'to' ISO-8601 date-times are required.");
                    }

                    return await HandleAsync(
                        new ReconciliationRequest { From = from.Value, To = to.Value },
                        cancellationToken);
                })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, CancellationToken cancellationToken)
    {
        var response = await _paymentService.GetReconciliationAsync(request.From, request.To, cancellationToken);
        return Results.Ok(response);
    }
}
