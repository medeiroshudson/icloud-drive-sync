using System.Text.Json.Serialization;

namespace ICloudDriveSync.Drive;

/// <summary>Nó do iCloud Drive (espelha o item retornado pelo retrieveItemDetailsInFolders).</summary>
public sealed record DriveNode(
    [property: JsonPropertyName("drivewsid")] string DriveWsId,
    [property: JsonPropertyName("docwsid")] string? DocWsId,
    [property: JsonPropertyName("parentDrivewsid")] string? ParentDriveWsId,
    [property: JsonPropertyName("etag")] string Etag,
    [property: JsonPropertyName("extension")] string? Extension,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("dateChanged")] DateTimeOffset? DateChanged,
    [property: JsonPropertyName("dateModified")] DateTimeOffset? DateModified,
    [property: JsonPropertyName("fileCount")] long? FileCount,
    [property: JsonPropertyName("restorePath")] string? RestorePath = null)
{
    public bool IsFolder => Type == "FOLDER";

    /// <summary>
    /// Nome efetivo no disco/árvore: espelha o DriveNode do pyicloud
    /// (name + extension, sem duplicar quando o name já embute a extensão).
    /// </summary>
    public string EffectiveName =>
        !string.IsNullOrEmpty(Extension) && !Name.EndsWith("." + Extension, StringComparison.Ordinal)
            ? $"{Name}.{Extension}"
            : Name;
}