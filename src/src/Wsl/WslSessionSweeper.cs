using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Purview.Containers.Runtime;

namespace Purview.Containers.Wsl;

/// <summary>
/// Removes crash residue left by earlier processes: WSLC sessions whose creator process is gone, and
/// the per-session storage directories those processes left behind. A live session is never touched.
/// </summary>
/// <remarks>
/// The process-exit hook terminates a runtime's own session on a graceful exit. A test host killed by a
/// timeout, a debugger stop or a CI job cancellation cannot run that hook, so its session and (for a
/// per-session store) its storage directory survive. WSLC keeps those sessions registered and their
/// VHDs on disk; across enough runs they fill the drive. The sweep runs once per process before the
/// first session is created and uses the <c>wslc</c> CLI when it is available; a host without the CLI
/// simply skips it.
/// </remarks>
static partial class WslSessionSweeper
{
	const string SessionNamePattern = @"^(?:wslc|purview)-(?<pid>\d+)-(?<token>[0-9a-f]{8})$";

	[GeneratedRegex(SessionNamePattern, RegexOptions.CultureInvariant)]
	private static partial Regex SessionNameRegex();

	/// <summary>Total budget for a sweep: listing sessions plus a bounded terminate per session.</summary>
	static readonly TimeSpan SweepTimeout = TimeSpan.FromSeconds(30);

	/// <summary>Budget for terminating one session.</summary>
	static readonly TimeSpan TerminateTimeout = TimeSpan.FromSeconds(10);

	internal static async Task<WslSessionSweepResult> SweepAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(SweepTimeout);
		var token = deadline.Token;
		var currentProcessId = Environment.ProcessId;

		WslCommand.Result listing;
		try
		{
			listing = await WslCommand
				.RunAsync(session: null, ["system", "session", "list"], token, allowFailure: true)
				.ConfigureAwait(false);
		}
		catch (Exception exception)
			when (exception
					is ContainerPrerequisiteException
						or Win32Exception
						or OperationCanceledException
						or TimeoutException
						or IOException
						or InvalidOperationException
			)
		{
			// A host without wslc, or a cancelled or timed-out listing, is not a session failure:
			// the SDK path still works and the next process will try again.
			return new(
				SessionsExamined: 0,
				TerminatedSessions: [],
				RemovedStorageDirectories: [],
				Failures: [$"Session sweep skipped: {exception.GetType().Name}: {exception.Message}"]
			);
		}

		if (listing.ExitCode != 0)
		{
			return new(
				SessionsExamined: 0,
				TerminatedSessions: [],
				RemovedStorageDirectories: [],
				Failures: [$"Session list failed with exit code {listing.ExitCode}."]
			);
		}

		var sessions = ParseSessions(Encoding.UTF8.GetString(listing.Output));
		var listedNames = sessions.Select(session => session.Name).ToHashSet(StringComparer.Ordinal);
		List<string> terminated = [];
		List<string> failures = [];

		foreach (var session in sessions)
		{
			if (session.CreatorProcessId == currentProcessId || IsProcessAlive(session.CreatorProcessId))
				continue;

			try
			{
				using var terminateDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
				terminateDeadline.CancelAfter(TerminateTimeout);
				var result = await WslCommand
					.RunAsync(
						session.Name,
						["system", "session", "terminate"],
						terminateDeadline.Token,
						allowFailure: true
					)
					.ConfigureAwait(false);
				if (result.ExitCode == 0)
				{
					terminated.Add(session.Name);
				}
				else
				{
					failures.Add($"{session.Name}: terminate exited with code {result.ExitCode}.");
				}
			}
			catch (Exception exception)
				when (exception
						is OperationCanceledException
							or TimeoutException
							or IOException
							or InvalidOperationException
							or Win32Exception
				)
			{
				failures.Add($"{session.Name}: {exception.GetType().Name}: {exception.Message}");
			}
		}

		var removedDirectories = RemoveOrphanedStorageDirectories(currentProcessId, listedNames, terminated);
		return new(
			SessionsExamined: sessions.Count,
			TerminatedSessions: terminated,
			RemovedStorageDirectories: removedDirectories,
			Failures: failures
		);
	}

	/// <summary>True when <paramref name="name" /> is a session name this library created.</summary>
	internal static bool TryParseSessionName(string? name, out int creatorProcessId)
	{
		creatorProcessId = 0;
		if (string.IsNullOrEmpty(name))
			return false;

		var match = SessionNameRegex().Match(name);
		return match.Success
			&& int.TryParse(
				match.Groups["pid"].Value,
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out creatorProcessId
			)
			&& creatorProcessId > 0;
	}

	/// <summary>
	/// Parses <c>wslc system session list</c> output into the sessions this library owns. The CLI table
	/// is <c>ID  Creator PID  Display Name</c>; anything that is not one of our names is ignored, so the
	/// default <c>wslc-cli-*</c> session and sessions created by other tools are never considered.
	/// <c>purview-*</c> is the current prefix; <c>wslc-*</c> is retained so residue from an earlier
	/// release is swept too.
	/// </summary>
	internal static IReadOnlyList<SessionEntry> ParseSessions(string output)
	{
		List<SessionEntry> sessions = [];
		foreach (var rawLine in output.Split('\n'))
		{
			var line = rawLine.Trim();
			if (line.Length == 0)
				continue;
			if (line.StartsWith("ID", StringComparison.OrdinalIgnoreCase))
				continue;
			if (line.StartsWith("[wslc]", StringComparison.OrdinalIgnoreCase))
				continue;

			var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (columns.Length < 3)
				continue;
			if (!int.TryParse(columns[1], NumberStyles.None, CultureInfo.InvariantCulture, out var listedProcessId))
				continue;

			var name = columns[^1];
			if (!TryParseSessionName(name, out var nameProcessId) || nameProcessId != listedProcessId)
				continue;

			sessions.Add(new(name, listedProcessId));
		}

		return sessions;
	}

	/// <summary>True when a process with <paramref name="processId" /> is still running.</summary>
	internal static bool IsProcessAlive(int processId)
	{
		if (processId <= 0)
			return false;

		try
		{
			using var process = Process.GetProcessById(processId);
			return !process.HasExited;
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
		catch (Win32Exception)
		{
			// The process exists but cannot be inspected. Be conservative: never delete a session
			// that might still be live.
			return true;
		}
	}

	/// <summary>
	/// Removes the per-session storage directories left by dead processes. A directory is removed only
	/// when its session was terminated by this sweep, or when no such session is registered at all. A
	/// directory whose session is still listed (because termination failed) is retained.
	/// </summary>
	internal static IReadOnlyList<string> RemoveOrphanedStorageDirectories(
		int currentProcessId,
		IReadOnlySet<string> listedSessionNames,
		IReadOnlyCollection<string> terminatedSessionNames,
		string? root = null
	)
	{
		root ??= WslContainerRuntime.PerSessionRoot;
		if (!Directory.Exists(root))
			return [];

		HashSet<string> terminated = new(terminatedSessionNames, StringComparer.Ordinal);
		List<string> removed = [];
		foreach (var directory in Directory.EnumerateDirectories(root))
		{
			var name = Path.GetFileName(directory);
			if (!TryParseSessionName(name, out var processId))
				continue;
			if (processId == currentProcessId || IsProcessAlive(processId))
				continue;
			if (listedSessionNames.Contains(name) && !terminated.Contains(name))
				continue;

			try
			{
				Directory.Delete(directory, recursive: true);
				removed.Add(name);
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
		}

		return removed;
	}

	internal sealed record SessionEntry(string Name, int CreatorProcessId);
}

/// <summary>The result of one orphaned-session sweep.</summary>
/// <param name="SessionsExamined">Number of this library's sessions found in the WSLC session list.</param>
/// <param name="TerminatedSessions">Sessions terminated because their creator process was gone.</param>
/// <param name="RemovedStorageDirectories">Per-session storage directories deleted from disk.</param>
/// <param name="Failures">Per-session failures; a failure never aborts the rest of the sweep.</param>
public sealed record WslSessionSweepResult(
	int SessionsExamined,
	IReadOnlyList<string> TerminatedSessions,
	IReadOnlyList<string> RemovedStorageDirectories,
	IReadOnlyList<string> Failures
)
{
	/// <summary>True when at least one orphaned session or directory could not be removed.</summary>
	public bool HasFailures => Failures.Count > 0;
}
