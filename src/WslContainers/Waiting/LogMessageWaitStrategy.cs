using System.Text.RegularExpressions;

namespace Purview.WslContainers;

/// <summary>Waits until the container's accumulated init-process output matches a message or regex.</summary>
public sealed class LogMessageWaitStrategy : WaitStrategy
{
	private readonly Regex pattern;

	public LogMessageWaitStrategy(string message)
	{
		ArgumentNullException.ThrowIfNull(message);
		pattern = new Regex(Regex.Escape(message), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	}

	public LogMessageWaitStrategy(Regex pattern)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		this.pattern = pattern;
	}

	/// <inheritdoc />
	public override async Task<bool> UntilAsync(WaitContext context, CancellationToken cancellationToken)
	{
		string logs = await context.Container.GetLogsAsync(stream: null, cancellationToken).ConfigureAwait(false);
		return pattern.IsMatch(logs);
	}
}
