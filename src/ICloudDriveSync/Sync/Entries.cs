using ICloudDriveSync.Drive;

namespace ICloudDriveSync.Sync;

/// <summary>Entrada da árvore local (mtime em UTC já arredondado pela camada de scan).</summary>
public sealed record LocalEntry(DateTimeOffset ModifiedUtc, long Size, bool IsDirectory);

/// <summary>Entrada da árvore remota (dateModified em UTC).</summary>
public sealed record RemoteEntry(DateTimeOffset DateModifiedUtc, long Size, bool IsDirectory, DriveNode Node);

public abstract record SyncAction(string Path);

public sealed record UploadAction(string Path) : SyncAction(Path);

public sealed record DownloadAction(string Path) : SyncAction(Path);

public sealed record MkDirCloudAction(string Path) : SyncAction(Path);

public sealed record MkDirLocalAction(string Path) : SyncAction(Path);

public sealed record DeleteLocalAction(string Path) : SyncAction(Path);
