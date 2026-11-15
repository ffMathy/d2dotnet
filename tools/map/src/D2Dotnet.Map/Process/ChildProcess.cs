using System.Diagnostics;

namespace D2Dotnet.Map.Process;

/// <summary>Result of <see cref="ChildProcess.RunAsync"/>.</summary>
public sealed record ChildProcessResult(string Cmd, IReadOnlyList<string> Args, int ExitCode, string Stdout, string Stderr, double Duration);

/// <summary>Run a command to completion collecting its output (upstream <c>child.process.ts</c>).</summary>
public static class ChildProcess
{
    public static async Task<ChildProcessResult> RunAsync(string cmd, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        var startTime = Stopwatch.GetTimestamp();
        var psi = new ProcessStartInfo(cmd)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var child = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {cmd}");

        var stdout = child.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = child.StandardError.ReadToEndAsync(cancellationToken);
        await child.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ChildProcessResult(
            cmd,
            args,
            child.ExitCode,
            (await stdout.ConfigureAwait(false)).Trim(),
            (await stderr.ConfigureAwait(false)).Trim(),
            Stopwatch.GetElapsedTime(startTime).TotalMilliseconds);
    }
}
