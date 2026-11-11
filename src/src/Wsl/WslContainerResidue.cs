using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Purview.Containers.Wsl;

/// <summary>
/// Removes containers left in the shared store by processes that exited without deleting them. Only a
/// generated name whose embedded creator PID is gone and that carries this library's owner label is
/// eligible; a live process's container is never touched.
/// </summary>
static partial class WslContainerResidue
{
	const string GeneratedContainerNamePattern = @"-(?<pid>\d+)-(?<token>[0-9a-f]{6})$";

	[GeneratedRegex(GeneratedContainerNamePattern, RegexOptions.CultureInvariant)]
	private static partial Regex GeneratedContainerNameRegex();

	internal static async Task<int> RemoveAsync(string sessionName, CancellationToken cancellationToken)
	{
		WslCommand.Result listing;
		using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
		{
			deadline.CancelAfter(TimeSpan.FromSeconds(20));
			listing = await WslCommand
				.RunAsync(sessionName, ["ps", "-a", "--format", "json"], deadline.Token, allowFailure: true)
				.ConfigureAwait(false);
		}

		if (listing.ExitCode != 0)
		{
			return 0;
		}

		var removed = 0;
		var output = Encoding.UTF8.GetString(listing.Output);
		foreach (var line in output.Split('\n'))
		{
			if (string.IsNullOrWhiteSpace(line))
				continue;

			string? id;
			string? name;
			try
			{
				using var document = JsonDocument.Parse(line);
				var root = document.RootElement;
				id = root.TryGetProperty("ID", out var idElement) ? idElement.GetString() : null;
				name = root.TryGetProperty("Names", out var nameElement) ? nameElement.GetString() : null;
			}
			catch (JsonException)
			{
				continue;
			}

			if (string.IsNullOrEmpty(id) || !IsStaleOwnedContainer(name))
				continue;

			try
			{
				using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				deadline.CancelAfter(TimeSpan.FromSeconds(10));
				var result = await WslCommand
					.RunAsync(sessionName, ["rm", "-f", id], deadline.Token, allowFailure: true)
					.ConfigureAwait(false);
				if (result.ExitCode == 0)
					removed++;
			}
			catch (Exception exception)
				when (exception
						is OperationCanceledException
							or TimeoutException
							or IOException
							or InvalidOperationException
				)
			{
				// Best effort: one stuck container must not stop the sweep.
			}
		}

		// Only anonymous volumes are pruned; named volumes are retained by design.
		using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
		{
			deadline.CancelAfter(TimeSpan.FromSeconds(20));
			_ = await WslCommand
				.RunAsync(sessionName, ["volume", "prune", "-f"], deadline.Token, allowFailure: true)
				.ConfigureAwait(false);
		}

		return removed;
	}

	internal static bool IsStaleOwnedContainer(string? name)
	{
		if (string.IsNullOrEmpty(name))
			return false;

		// SDK-created containers cannot carry labels (the managed API has no Labels setting), so the
		// generated name is the marker: {image}-{pid}-{6 hex}. A live process's PID is never removed.
		var match = GeneratedContainerNameRegex().Match(name);
		if (!match.Success)
			return false;
		if (!int.TryParse(match.Groups["pid"].Value, out var processId))
			return false;
		if (processId <= 0)
			return false;

		return processId != Environment.ProcessId && !WslSessionSweeper.IsProcessAlive(processId);
	}
}
