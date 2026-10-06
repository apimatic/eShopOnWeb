using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.eShopWeb.PublicApi.SquareEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.CatalogItemEndpoints;

public class UploadCatalogItemPhotoRequest : BaseRequest
{
    public UploadCatalogItemPhotoRequest(int catalogItemId, byte[] content, CancellationToken cancellationToken)
    {
        CatalogItemId = catalogItemId;
        Content = content;
        CancellationToken = cancellationToken;
    }

    public int CatalogItemId { get; }
    public byte[] Content { get; }
    public CancellationToken CancellationToken { get; }
}

public class UploadCatalogItemPhotoResponse : BaseResponse
{
    public UploadCatalogItemPhotoResponse(Guid correlationId) : base(correlationId)
    {
    }

    public UploadCatalogItemPhotoResponse()
    {
    }

    public int CatalogItemId { get; set; }
    public string SquareItemId { get; set; } = string.Empty;

    /// <summary>Square's id for the image.</summary>
    public string ImageId { get; set; } = string.Empty;

    /// <summary>The address Square serves the image from.</summary>
    public string? ImageUrl { get; set; }
}

/// <summary>
/// Uploads a product photo (JPEG or PNG, at most 5 MB, multipart field "photo") and makes it the photo of
/// the product's Square item (operator action). Anything else is rejected without calling Square.
/// </summary>
public class UploadCatalogItemPhotoEndpoint : IEndpoint<IResult, UploadCatalogItemPhotoRequest, SquarePhotoService>
{
    public const string FormFieldName = "photo";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/catalog-items/{catalogItemId}/photo",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int catalogItemId, HttpContext httpContext, SquarePhotoService photoService) =>
            {
                var (content, problem) = await ReadPhotoAsync(httpContext.Request);
                if (problem is not null)
                {
                    return problem;
                }

                using var deadline = SquareRequestDeadline.Start(httpContext, SquareConstants.RequestBudget);
                return await HandleAsync(new UploadCatalogItemPhotoRequest(catalogItemId, content!, deadline.Token), photoService);
            })
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<UploadCatalogItemPhotoResponse>()
            .WithTags("CatalogItemEndpoints");
    }

    public async Task<IResult> HandleAsync(UploadCatalogItemPhotoRequest request, SquarePhotoService photoService)
    {
        var result = await photoService.UploadAsync(request.CatalogItemId, request.Content, request.CancellationToken);
        return Results.Ok(new UploadCatalogItemPhotoResponse(request.CorrelationId())
        {
            CatalogItemId = result.CatalogItemId,
            SquareItemId = result.SquareItemId,
            ImageId = result.ImageId,
            ImageUrl = result.ImageUrl,
        });
    }

    /// <summary>Validates the upload before anything else happens: multipart, field "photo", size, JPEG/PNG signature.</summary>
    private static async Task<(byte[]? Content, IResult? Problem)> ReadPhotoAsync(HttpRequest request)
    {
        if (!request.HasFormContentType || request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true)
        {
            return (null, Error(StatusCodes.Status415UnsupportedMediaType, $"Send the photo as multipart/form-data in a field named '{FormFieldName}'."));
        }

        // Generous envelope for multipart overhead; the file itself is checked against the exact limit below.
        if (request.ContentLength > SquareConstants.MaxPhotoBytes + 64 * 1024)
        {
            return (null, Error(StatusCodes.Status413PayloadTooLarge, "The photo must be at most 5 MB."));
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
        }
        catch (InvalidDataException)
        {
            return (null, Error(StatusCodes.Status400BadRequest, "The multipart body could not be read."));
        }

        var file = form.Files.GetFile(FormFieldName);
        if (file is null || file.Length == 0)
        {
            return (null, Error(StatusCodes.Status400BadRequest, $"No file in the multipart field '{FormFieldName}'."));
        }

        if (file.Length > SquareConstants.MaxPhotoBytes)
        {
            return (null, Error(StatusCodes.Status413PayloadTooLarge, "The photo must be at most 5 MB."));
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, request.HttpContext.RequestAborted);
        var content = buffer.ToArray();
        if (SquarePhotoService.DetectFormat(content) is null)
        {
            return (null, Error(StatusCodes.Status415UnsupportedMediaType, "The photo must be a JPEG or PNG image."));
        }

        return (content, null);
    }

    private static IResult Error(int statusCode, string message) =>
        Results.Content(new BlazorShared.Models.ErrorDetails { StatusCode = statusCode, Message = message }.ToString(), "application/json", statusCode: statusCode);
}
