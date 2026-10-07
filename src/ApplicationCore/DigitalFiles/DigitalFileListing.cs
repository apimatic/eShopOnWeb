using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.DigitalFiles;

/// <summary>
/// The files offered as digital products. <see cref="IsComplete"/> is false when the listing stopped at its
/// page cap or when some entries could not be read; in that case <see cref="Files"/> is not the whole folder.
/// </summary>
public sealed record DigitalFileListing(
    IReadOnlyList<DigitalFileInfo> Files,
    bool IsComplete,
    IReadOnlyList<UnreadableDigitalFileEntry> UnreadableEntries);

/// <summary>An entry in the product folder that could not be read as a file.</summary>
public sealed record UnreadableDigitalFileEntry(string? Id, string? Name, string Reason);
