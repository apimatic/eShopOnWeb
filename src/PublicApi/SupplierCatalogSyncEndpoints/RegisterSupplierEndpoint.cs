using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierCatalogAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierCatalogSyncEndpoints;

/// <summary>
/// Registers a supplier and the URL of its product listing page. Operator-only.
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
            .WithTags("SupplierCatalogSyncEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterSupplierRequest request, IRepository<Supplier> supplierRepository)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest("A supplier 'name' is required.");

        if (string.IsNullOrWhiteSpace(request.ProductListingUrl))
            return Results.BadRequest("A 'productListingUrl' is required.");

        if (!Uri.TryCreate(request.ProductListingUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.BadRequest("'productListingUrl' must be an absolute http(s) URL.");
        }

        var supplier = new Supplier(request.Name.Trim(), request.ProductListingUrl.Trim());
        supplier = await supplierRepository.AddAsync(supplier);

        var response = new RegisterSupplierResponse
        {
            SupplierId = supplier.Id,
            Name = supplier.Name,
            ProductListingUrl = supplier.ProductListingUrl
        };

        return Results.Created($"api/catalog/suppliers/{supplier.Id}", response);
    }
}
