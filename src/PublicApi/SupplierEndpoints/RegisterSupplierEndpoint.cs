using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Registers a supplier: a name and the URL of its product listing page. Operator-only.
/// </summary>
public class RegisterSupplierEndpoint : IEndpoint<IResult, RegisterSupplierRequest, IRepository<Supplier>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterSupplierRequest request, IRepository<Supplier> supplierRepository) =>
            {
                return await HandleAsync(request, supplierRepository);
            })
            .Produces<RegisterSupplierResponse>(StatusCodes.Status201Created)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterSupplierRequest request, IRepository<Supplier> supplierRepository)
    {
        var response = new RegisterSupplierResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest("A supplier name is required.");
        }

        if (!IsValidHttpUrl(request.ListingUrl))
        {
            return Results.BadRequest("A valid absolute http(s) listing URL is required.");
        }

        var supplier = new Supplier(request.Name.Trim(), request.ListingUrl.Trim());
        supplier = await supplierRepository.AddAsync(supplier);

        response.SupplierId = supplier.Id;
        response.Name = supplier.Name;
        response.ListingUrl = supplier.ListingUrl;

        return Results.Created($"api/catalog/suppliers/{supplier.Id}", response);
    }

    private static bool IsValidHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
