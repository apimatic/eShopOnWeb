using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, IRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{id:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int id,
                   IRepository<SavedPaymentMethod> repo,
                   HttpContext ctx,
                   CancellationToken ct) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var spec = new SavedPaymentMethodByIdAndBuyerSpec(id, buyerId);
                var method = await repo.FirstOrDefaultAsync(spec, ct);
                if (method == null)
                    return Results.NotFound(new { error = $"Payment method {id} not found." });

                await repo.DeleteAsync(method, ct);
                return Results.NoContent();
            })
            .Produces(204)
            .Produces(404)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int request, IRepository<SavedPaymentMethod> service)
        => throw new NotImplementedException();
}
