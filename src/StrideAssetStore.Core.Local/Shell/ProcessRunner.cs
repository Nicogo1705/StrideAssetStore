// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Diagnostics;

namespace StrideAssetStore.Core.Local.Shell;

/// <summary>What a child process produced. <paramref name="ExitCode"/> is -1 when it never started.</summary>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Runs a command-line tool (git, gh) and collects its output.
/// </summary>
/// <remarks>
/// Never waits forever. The app has no console and no cancel button, so a child that blocks —
/// classically <c>git push</c> stopping for credentials — used to freeze the page it was started
/// from until the process was killed from the Task Manager. Prompting is disabled outright, and a
/// deadline kills the whole tree as a backstop.
/// </remarks>
public static class ProcessRunner
{
    /// <summary>Default deadline. Generous enough for a slow clone or push over a poor connection.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    public static async Task<ProcessResult> RunAsync(
        string exe,
        IReadOnlyList<string> args,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellation = default)
    {
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in args)
        {
            info.ArgumentList.Add(argument);
        }

        // Fail instead of blocking on a prompt nobody can answer: git asks on the terminal it does
        // not have, and the Git Credential Manager would pop a window behind the browser.
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GCM_INTERACTIVE"] = "never";

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return new ProcessResult(-1, "", $"Unable to start '{exe}'.");
            }

            // Both streams concurrently — sequential reads deadlock when the child fills the stderr
            // pipe while we are still draining stdout (verbose git push output).
            var stdout = process.StandardOutput.ReadToEndAsync(cancellation);
            var stderr = process.StandardError.ReadToEndAsync(cancellation);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(timeout ?? DefaultTimeout);

            try
            {
                await process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                return new ProcessResult(-1, "", cancellation.IsCancellationRequested
                    ? $"{exe} was cancelled."
                    : $"{exe} did not finish within {(timeout ?? DefaultTimeout).TotalMinutes:0} minutes and was stopped.");
            }

            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
        catch (Exception ex)
        {
            // Executable not found on PATH, blocked by policy, etc.
            return new ProcessResult(-1, "", ex.Message);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true); // git spawns helpers; leaving them holds the repo lock
        }
        catch
        {
            // Already gone, or we lost the race with its exit.
        }
    }
}
