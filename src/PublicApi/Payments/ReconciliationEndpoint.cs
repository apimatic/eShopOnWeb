using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, OrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (DateTimeOffset? from, DateTimeOffset? to, OrderPaymentService service) =>
                {
                    if (from is null || to is null)
                        throw PaymentException.BadRequest("Both 'from' and 'to' ISO-8601 date-times are required.");
                    return await HandleAsync(new ReconciliationRequest { From = from.Value, To = to.Value }, service);
                })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, OrderPaymentService service)
    {
        var response = await service.GetReconciliationAsync(request.From, request.To);
        return Results.Ok(response);
    }
}
