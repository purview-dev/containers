using Purview.WslContainers.Diagnostics;
using Purview.WslContainers.Runtime;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Purview.WslContainers.Waiting;

/// <summary>Runs wait strategies with timeout, interval and retries, producing actionable diagnostics on failure.</summary>
[SuppressMessage(
	"Design",
	"CA1031:Do not catch general exception types",
	Justification = "Wait checks observe transient errors; diagnostics collection is best-effort."
)]
static class WaitStrategyRunner
{
	public static async Task RunAsync(
		WaitContext context,
		IReadOnlyList<IWaitStrategy> strategies,
		TimeSpan defaultTimeout,
		CancellationToken cancellationToken
	)
	{
		foreach (var strategy in strategies)
		{
			await RunSingleAsync(context, strategy, defaultTimeout, cancellationToken).ConfigureAwait(false);
		}
	}

	static async Task RunSingleAsync(
		WaitContext context,
		IWaitStrategy strategy,
		TimeSpan defaultTimeout,
		CancellationToken cancellationToken
	)
	{
		using var activity = WslContainersActivity.Source.StartActivity("wslcontainer.wait");
		activity?.SetTag("container.name", context.Container.Name);
		activity?.SetTag("strategy", strategy.GetType().Name);
		var timeout = strategy.Timeout ?? defaultTimeout;
		var interval = strategy.Interval > TimeSpan.Zero ? strategy.Interval : TimeSpan.FromMilliseconds(250);
		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken
		);
		timeoutSource.CancelAfter(timeout);
		var token = timeoutSource.Token;

		Exception? lastError = null;
		var failedChecks = 0;
		while (!token.IsCancellationRequested)
		{
			bool ready;
			try
			{
				ready = await strategy.UntilAsync(context, token).ConfigureAwait(false);
				if (ready)
				{
					return;
				}
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				break;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				lastError = ex;
			}

			failedChecks++;
			if (strategy.Retries is int retries && retries > 0 && failedChecks > retries)
			{
				break;
			}

			try
			{
				await Task.Delay(interval, token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) { }
		}
		cancellationToken.ThrowIfCancellationRequested();

		var diagnostics = await BuildDiagnosticsAsync(context, timeout, lastError, cancellationToken).ConfigureAwait(false);
		throw new WslContainerTimeoutException(
			$"Container '{context.Container.Name}' did not become ready within {timeout} for strategy {strategy.GetType().Name}. {diagnostics}"
		);
	}

	static async Task<string> BuildDiagnosticsAsync(WaitContext context, TimeSpan timeout, Exception? lastError, CancellationToken cancellationToken)
	{
		StringBuilder builder = new();
		builder.Append(
			CultureInfo.InvariantCulture,
			$"State: {context.Container.State}. Image: {context.Container.Image}. Startup timeout: {timeout}."
		);

		try
		{
			var ports = context.Container.GetMappedPublicPorts();
			if (ports.Count > 0)
			{
				builder.Append(
					CultureInfo.InvariantCulture,
					$" Mapped ports: {string.Join(", ", ports.Select(pair => $"{pair.Key}->{pair.Value}"))}."
				);
			}
		}
		catch { }

		if (lastError is not null)
		{
			builder.Append(
				CultureInfo.InvariantCulture,
				$" Last check error: {lastError.GetType().Name}: {lastError.Message}."
			);
		}

		try
		{
			var logs = await context
				.Container.GetLogsAsync(stream: null, cancellationToken)
				.ConfigureAwait(false);
			var tail = logs.Length > 2000 ? logs[^2000..] : logs;
			if (tail.Length > 0)
			{
				builder.Append(CultureInfo.InvariantCulture, $" Recent output: {tail}");
			}
		}
		catch { }

		return builder.ToString();
	}
}
