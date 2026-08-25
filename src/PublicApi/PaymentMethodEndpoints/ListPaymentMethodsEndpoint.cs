using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record SavedCardDto(int PaymentMethodId, string? Last4, string? Brand, string? Expiry);

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(System.Guid correlationId) : base(correlationId) { }
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IReadRepository<Buyer> buyerRepo, HttpContext httpContext, CancellationToken ct) =>
            {
                var buyerId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var buyer = await buyerRepo.FirstOrDefaultAsync(new BuyerByIdentitySpec(buyerId), ct);
                var dtos = new List<SavedCardDto>();
                if (buyer != null)
                {
                    foreach (var pm in buyer.PaymentMethods)
                        dtos.Add(new SavedCardDto(pm.Id, pm.Last4, pm.Brand, pm.Expiry));
                }

                return Results.Ok(new ListPaymentMethodsResponse(Guid.NewGuid()) { PaymentMethods = dtos });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.StatusCode(501));
}
