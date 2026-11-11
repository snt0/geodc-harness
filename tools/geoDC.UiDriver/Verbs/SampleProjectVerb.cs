using System.IO;

namespace geoDC.UiDriver.Verbs;

/// <summary>
///     <c>sample create</c> and <c>sample status</c>: the sample projects under <c>dev/sample-projects/</c> in the
///     <see cref="DriverHome" />. Needs no running instance.
/// </summary>
internal static class SampleProjectVerb
{
    /// <summary>
    ///     <c>sample create [--lifecycle]</c> regenerates <c>dev/sample-projects/sample.gdcproj</c> (or
    ///     <c>sample-lifecycle.gdcproj</c>), its Shore Control Register and its provenance file with the test-data
    ///     generator CLI. <c>sample status</c> reports which sample projects exist and the fingerprints that produced
    ///     them. Both run from the geoDC repository root, as every verb does, so the generator builds from it.
    /// </summary>
    internal static void Run(string[] args)
    {
        string action = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
        CliArgs cli = new([action, .. args.Skip(2)]);
        string driverHome = DriverHome.Directory;
        switch (action)
        {
            case "create":
                Create(cli.Has("--lifecycle") ? DriverSampleProject.Lifecycle : DriverSampleProject.Default,
                    driverHome);
                break;
            case "status":
                JsonOutput.Success("sample", new
                {
                    action = "status",
                    sampleProjects = DriverSampleProject.All
                        .Select(sampleProject => Describe(sampleProject.ReadState(driverHome)))
                        .ToArray()
                });
                break;
            default:
                throw new DriverException("bad-arguments",
                    "Usage: geodc-ui sample create [--lifecycle] | sample status.", 1);
        }
    }

    private static void Create(DriverSampleProject sampleProject, string driverHome)
    {
        string staging = Path.Combine(Path.GetTempPath(), $"geodc-{sampleProject.Name}-sample-project-create");
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, true);
        }

        SampleProjectCreateLog log = new(sampleProject);
        try
        {
            IReadOnlyList<string> output = SampleProjectGenerator.Run(sampleProject, staging, log);
            SampleProjectProvenance provenance = sampleProject.ReadProvenance(output, DateTime.UtcNow);
            string? register = DriverSampleProject.GeneratedRegisterPath(output);
            sampleProject.Install(driverHome, staging, register, provenance);
            log.Event($"Installed {sampleProject.PackagePath} (seed {provenance.Seed}, project fingerprint " +
                      $"{provenance.ProjectFingerprint}), " +
                      (register is null
                          ? "no register"
                          : $"{sampleProject.RegisterPath} ({provenance.RegisterFingerprint})") +
                      $" and {sampleProject.ProvenancePath}.");
            JsonOutput.Success("sample", new
            {
                action = "create",
                sampleProject = Describe(sampleProject.ReadState(driverHome)),
                log = log.Path
            });
        }
        catch (DriverException ex)
        {
            log.Event($"Failed with {ex.Code}: {ex.Message}");
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Event($"Failed installing the sample project: {ex.Message}");
            throw;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }

            SaveLog(log);
        }
    }

    // The log is diagnostics only: a run that could not write it still reports its own outcome, and says why on stderr.
    private static void SaveLog(SampleProjectCreateLog log)
    {
        try
        {
            log.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"geodc-ui: could not write the sample project log {log.Path}: {ex.Message}");
        }
    }

    private static object Describe(SampleProjectState state)
    {
        return new
        {
            name = state.SampleProject.Name,
            preset = state.SampleProject.Preset,
            package = state.SampleProject.PackagePath,
            exists = state.PackageExists,
            register = state.RegisterExists ? state.SampleProject.RegisterPath : null,
            provenanceFile = state.Provenance is null ? null : state.SampleProject.ProvenancePath,
            ready = state.Ready,
            seed = state.Provenance?.Seed,
            projectFingerprint = state.Provenance?.ProjectFingerprint,
            domainFingerprint = state.Provenance?.DomainFingerprint,
            registerFingerprint = state.Provenance?.RegisterFingerprint,
            generatedUtc = state.Provenance?.GeneratedUtc
        };
    }
}