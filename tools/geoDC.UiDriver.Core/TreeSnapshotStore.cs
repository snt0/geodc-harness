using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace geoDC.UiDriver;

/// <summary>
///     The arguments that decide what a <c>tree</c> reads, and so which stored snapshot a later <c>tree --diff</c>
///     compares against. <c>--diff</c>, <c>--bounds</c> and <c>--timeout</c> are not part of it.
/// </summary>
/// <param name="Target">The UI driver target name, <c>app</c> or <c>sample</c>.</param>
/// <param name="Id">The <c>--id</c> whose subtree is read, or null.</param>
/// <param name="Scope">The <c>--scope</c> value, or null.</param>
/// <param name="Window">The <c>--window</c> selector as given, or null.</param>
/// <param name="MainWindow">Whether <c>--main-window</c> was given.</param>
/// <param name="Depth">The walk depth after defaults are applied; null for no limit.</param>
/// <param name="Interactable">Whether <c>--interactable</c> was given.</param>
public sealed record TreeSnapshotKey(
    string Target,
    string? Id,
    string? Scope,
    string? Window,
    bool MainWindow,
    int? Depth,
    bool Interactable)
{
    private static readonly JsonSerializerOptions CanonicalOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     The key as compact JSON, so no argument value can make two different keys read the same. The stored
    ///     snapshot carries it to confirm it belongs to this key.
    /// </summary>
    [JsonIgnore]
    public string Canonical => JsonSerializer.Serialize(this, CanonicalOptions);

    /// <summary>
    ///     <c>&lt;target&gt;-&lt;hash&gt;.json</c>, where the hash is the first 16 hexadecimal digits of the SHA-256 of
    ///     <see cref="Canonical" />.
    /// </summary>
    [JsonIgnore]
    public string FileName =>
        $"{Target}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical)))[..16]}.json";

    /// <summary>The key of a <c>tree</c> invocation run against <paramref name="target" />.</summary>
    public static TreeSnapshotKey From(string target, CliArgs cli, TreeOptions options)
    {
        return new TreeSnapshotKey(target, cli.Value("--id"), cli.Value("--scope"), cli.Value("--window"),
            cli.Has("--main-window"), options.Depth, options.Interactable);
    }
}

/// <summary>
///     The outcome of reading a stored snapshot.
/// </summary>
/// <param name="Roots">The stored roots, or null when there is no usable snapshot.</param>
/// <param name="Problem">
///     Why an existing file could not be used (unreadable, malformed, another format version or key); null when the
///     snapshot was read or there was none.
/// </param>
public sealed record TreeSnapshotRead(IReadOnlyList<UiaNodeSnapshot>? Roots, string? Problem);

/// <summary>
///     Stores the last <c>tree</c> result per <see cref="TreeSnapshotKey" /> as JSON files in one directory, by default
///     <c>.ui-driver/snapshots/</c>. Each write replaces the previous snapshot for its key atomically, so a reader sees
///     either the old or the new file.
/// </summary>
public sealed class TreeSnapshotStore(string directory)
{
    /// <summary>The file format version; a file written with another version is treated as absent.</summary>
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true
    };

    /// <summary>The directory the snapshot files live in, as given; it is created when a snapshot is first written.</summary>
    public string DirectoryPath { get; } = directory;

    /// <summary>The store under <c>.ui-driver/snapshots/</c> in the driver home.</summary>
    public static TreeSnapshotStore Default()
    {
        return new TreeSnapshotStore(DriverStateDirectory.Resolve("snapshots"));
    }

    /// <summary>
    ///     The file that holds the snapshot for <paramref name="key" />: <see cref="TreeSnapshotKey.FileName" /> under
    ///     <see cref="DirectoryPath" />, whether or not it exists.
    /// </summary>
    public string PathFor(TreeSnapshotKey key)
    {
        return Path.Combine(DirectoryPath, key.FileName);
    }

    /// <summary>
    ///     Reads the snapshot stored for <paramref name="key" />. A missing file gives no snapshot and no problem. A
    ///     file that cannot be used gives no snapshot and the reason. File and content errors are reported this way,
    ///     not thrown.
    /// </summary>
    public TreeSnapshotRead Read(TreeSnapshotKey key)
    {
        string path = PathFor(key);
        if (!File.Exists(path))
        {
            return new TreeSnapshotRead(null, null);
        }

        try
        {
            SnapshotFile? file = JsonSerializer.Deserialize<SnapshotFile>(File.ReadAllText(path), JsonOptions);
            if (file is null)
            {
                return new TreeSnapshotRead(null, "the file holds no snapshot");
            }

            if (file.Version != FormatVersion)
            {
                return new TreeSnapshotRead(null,
                    $"the file has format version {file.Version}, not {FormatVersion}");
            }

            return file.Key == key
                ? new TreeSnapshotRead(file.Roots, null)
                : new TreeSnapshotRead(null, "the file was stored for different tree arguments");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException)
        {
            return new TreeSnapshotRead(null, ex.Message);
        }
    }

    /// <summary>
    ///     Stores <paramref name="roots" /> as the snapshot for <paramref name="key" />, creating the directory when
    ///     needed, and returns the file path. Throws <see cref="IOException" /> or
    ///     <see cref="UnauthorizedAccessException" /> when the file cannot be written.
    /// </summary>
    public string Write(TreeSnapshotKey key, IReadOnlyList<UiaNodeSnapshot> roots)
    {
        string path = PathFor(key);
        Directory.CreateDirectory(DirectoryPath);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath,
                JsonSerializer.Serialize(new SnapshotFile(FormatVersion, key, roots), JsonOptions));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        return path;
    }

    private sealed record SnapshotFile(int Version, TreeSnapshotKey Key, IReadOnlyList<UiaNodeSnapshot> Roots);
}