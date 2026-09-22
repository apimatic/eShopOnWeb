using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number the shopper wants on file (any form the provider can canonicalize).</summary>
    public string Number { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
}

/// <summary>Registers a mobile number for the signed-in shopper (rejects unusable numbers up front).</summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext http, IContactNumberService service, System.Threading.CancellationToken ct) =>
            {
                var ownerId = CallerContext.GetUserName(http);
                if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(request?.Number)) return Results.BadRequest("A number is required.");

                try
                {
                    var id = await service.RegisterAsync(ownerId, request.Number, ct);
                    return Results.Created($"api/contact-numbers/{id}",
                        new RegisterContactNumberResponse { ContactNumberId = id });
                }
                catch (ContactNumberNotUsableException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
                catch (ProviderGatewayException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }
}
