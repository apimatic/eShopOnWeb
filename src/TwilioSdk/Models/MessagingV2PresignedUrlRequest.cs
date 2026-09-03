using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record MessagingV2PresignedUrlRequest
{
    /// <summary>
    /// Base64-encoded MD5 hash of the image
    /// </summary>
    [JsonPropertyName("imageContentMd5")]
    public required string ImageContentMd5 { get; init; }

    /// <summary>
    /// MIME type of the image (e.g., image/png, image/jpeg)
    /// </summary>
    [JsonPropertyName("imageContentType")]
    public required string ImageContentType { get; init; }

    /// <summary>
    /// Type of image (logo, hero, etc.)
    /// </summary>
    [JsonPropertyName("imageKind")]
    public required string ImageKind { get; init; }

    /// <summary>
    /// Name of the image file
    /// </summary>
    [JsonPropertyName("imageName")]
    public required string ImageName { get; init; }

    /// <summary>
    /// Size of the image in bytes
    /// </summary>
    [JsonPropertyName("imageSizeBytes")]
    public required int ImageSizeBytes { get; init; }

    /// <summary>
    /// Height of the image in pixels
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("imageHeight")]
    public int? ImageHeight { get; init; }

    /// <summary>
    /// Width of the image in pixels
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("imageWidth")]
    public int? ImageWidth { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
