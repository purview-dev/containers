using System.Text.RegularExpressions;

namespace Purview.Containers.Waiting;

/// <summary>Waits until the container's accumulated init-process output matches a message or regex.</summary>
public sealed class LogMessageWaitStrategy : WaitStrategy
{
	readonly Regex _pattern;

	public LogMessageWaitStrategy(string message)
	{
		ArgumentNullException.ThrowIfNull(message);
		_pattern = new Regex(Regex.Escape(message), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	}

	public LogMessageWaitStrategy(Regex pattern)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		_pattern = pattern;
	}

	/// <inheritdoc />
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		var logs = await context.Container.GetLogsAsync(stream: null, cancellationToken).ConfigureAwait(false);
		return _pattern.IsMatch(logs);
	}
}
