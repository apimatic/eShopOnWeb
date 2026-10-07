namespace Microsoft.eShopWeb.ApplicationCore.DigitalFiles;

/// <summary>A file offered as a digital product.</summary>
public sealed record DigitalFileInfo(string Id, string Name, long? SizeBytes);
