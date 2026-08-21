using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }

    public List<SavedPaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>
/// GET /api/payment-methods — the caller's own saved cards. Shopper-scoped: a shopper never sees another's.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IReadRepository<SavedPaymentMethod> repository,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);

                var methods = await repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), ct);

                var response = new ListPaymentMethodsResponse(Guid.NewGuid())
                {
                    PaymentMethods = methods
                        .OrderByDescending(m => m.CreatedDate)
                        .Select(SavedPaymentMethodDto.From)
                        .ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}
