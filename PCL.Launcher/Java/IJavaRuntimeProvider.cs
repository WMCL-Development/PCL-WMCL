namespace PCL;

/// <summary>
/// Supplies the Java runtimes a platform can download itself. Desktop scans the host and can
/// also fetch a runtime through the launcher's existing downloader, so it leaves this unset;
/// Android hosts a runtime from its own release feed and implements it.
/// </summary>
public interface IJavaRuntimeProvider
{
    /// <summary>Whether this platform can download a runtime at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Choices shown to the player, in display order.</summary>
    IReadOnlyList<string> Options { get; }

    /// <summary>
    /// Downloads and unpacks the chosen runtime, returning the Java home to register. Reports
    /// the current stage and a 0..1 value.
    /// </summary>
    Task<string> InstallAsync(int optionIndex, Action<string, double> progress, CancellationToken token);

    /// <summary>
    /// Unpacks a runtime archive the player picked and returns the Java home inside it.
    /// Platforms differ in which archive formats they can handle.
    /// </summary>
    Task<string> ImportArchiveAsync(string archivePath, CancellationToken token);
}
