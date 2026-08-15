using System;
using System.IO;

namespace IQIAIndicator.Tests.Calibration;

/// <summary>
/// Sprint 15.17.1: test-only isolation helper. Creates a fresh, uniquely-named directory under
/// Path.GetTempPath() and deletes it (recursively) on Dispose, so repeated test runs no longer leave
/// "iqia_sprint1517_*" folders behind under %TEMP% - exactly the litter that caused confusion when
/// real ATAS export output was being looked for (see QDE-012 Sprint 15.17.1 report, root cause).
/// Tests must use this (or equivalent try/finally cleanup) instead of a bare Path.Combine(
/// Path.GetTempPath(), ...) with no cleanup.
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; }

    public TemporaryDirectory(string prefix = "iqia_test_")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup only - a locked file must never fail the test itself.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
