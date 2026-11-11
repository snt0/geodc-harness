using System.Text.Json;

namespace geoDC.UiDriver;

/// <summary>
///     What the UI driver's instance file holds: the process the UI driver launched and the arguments it launched it
///     with. The process fields identify the instance. The launch fields let <c>rebuild</c> launch it again the same
///     way.
/// </summary>
/// <param name="Target">The <see cref="DriverTarget.Name" /> it was launched for.</param>
/// <param name="Pid">The launched process id.</param>
/// <param name="StartTimeUtc">The process start time, which tells it apart from a later process given the same id.</param>
/// <param name="ExecutablePath">The full path of the executable that was launched.</param>
/// <param name="Passive">True when it was launched with <c>--passive</c>.</param>
/// <param name="ProjectPath">The full path of the project package it opened, or null for a target that opens none.</param>
/// <param name="WorkingDirectory">The UI driver's working directory at launch: the worktree that owns it.</param>
/// <remarks>The launch fields are optional, so an instance file that lacks them still reads.</remarks>
public sealed record DriverInstanceRecord(
    string Target,
    int Pid,
    DateTime StartTimeUtc,
    string ExecutablePath,
    bool Passive = false,
    string? ProjectPath = null,
    string? WorkingDirectory = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    ///     Reads an instance file's contents. Returns null for content that is not an instance record: not JSON, or
    ///     without a target, a positive process id or an executable path.
    /// </summary>
    public static DriverInstanceRecord? Parse(string json)
    {
        try
        {
            DriverInstanceRecord? record = JsonSerializer.Deserialize<DriverInstanceRecord>(json, JsonOptions);
            return record is { Target.Length: > 0, Pid: > 0, ExecutablePath.Length: > 0 } ? record : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The instance file's contents.</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }
}