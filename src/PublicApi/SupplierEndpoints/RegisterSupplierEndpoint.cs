using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Registers a supplier (name + product listing URL) whose catalog can later be synced.
/// Restricted to administrators.
/// </summary>
public class RegisterSupplierEndpoint : IEndpoint<IResult, RegisterSupplierRequest, ISupplierCatalogSyncService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterSupplierRequest request, ISupplierCatalogSyncService syncService) =>
            {
                return await HandleAsync(request, syncService);
            })
            .Produces<RegisterSupplierResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterSupplierRequest request, ISupplierCatalogSyncService syncService)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest("A supplier name is required.");
        }

        if (!Uri.TryCreate(request.ListingUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.BadRequest("A valid absolute http or https listing URL is required.");
        }

        var supplier = await syncService.RegisterSupplierAsync(request.Name.Trim(), request.ListingUrl.Trim());

        var response = new RegisterSupplierResponse(request.CorrelationId())
        {
            SupplierId = supplier.Id,
            Name = supplier.Name,
            ListingUrl = supplier.ListingUrl,
            RegisteredAt = supplier.RegisteredAt
        };

        return Results.Created($"api/catalog/suppliers/{supplier.Id}", response);
    }
}
