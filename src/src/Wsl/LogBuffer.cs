using System.Text;
using System.Threading.Channels;
using Microsoft.WSL.Containers;

namespace Purview.Containers.Wsl;

/// <summary>
/// Bounded buffer for init-process output. Accumulates decoded lines for diagnostics while publishing
/// every entry to a channel so wait strategies and consumers can tail live output.
/// </summary>
sealed class LogBuffer
{
	const int MaxEntries = 4096;
	readonly Lock _sync = new();
	readonly List<ContainerLogEntry> _entries = [with(MaxEntries)];
	readonly Channel<ContainerLogEntry> _channel = Channel.CreateBounded<ContainerLogEntry>(
		new BoundedChannelOptions(1024)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
			SingleWriter = false,
		}
	);
	readonly Decoder _stdoutDecoder = Encoding.UTF8.GetDecoder();
	readonly Decoder _stderrDecoder = Encoding.UTF8.GetDecoder();
	readonly StringBuilder _stdoutPending = new();
	readonly StringBuilder _stderrPending = new();
	int _attached;

	public void Attach(Process process)
	{
		if (Interlocked.Exchange(ref _attached, 1) == 1)
		{
			return;
		}

		process.OutputReceived += OnStdout;
		process.ErrorReceived += OnStderr;
		process.Exited += _ =>
		{
			FlushRemaining();
			Complete();
		};
	}

	public IReadOnlyList<ContainerLogEntry> Snapshot()
	{
		lock (_sync)
		{
			return _entries.ToArray();
		}
	}

	public string GetLogs(LogOutput? stream)
	{
		lock (_sync)
		{
			var source = stream is LogOutput s ? _entries.Where(entry => entry.Stream == s) : _entries;
			return string.Join('\n', source.Select(entry => entry.Text));
		}
	}

	public ChannelReader<ContainerLogEntry> Reader => _channel.Reader;

	/// <summary>
	/// Completes the live stream. Call once no further output can arrive (init process exit or container
	/// disposal) so consumers tailing <see cref="Reader" /> finish instead of waiting forever.
	/// </summary>
	public void Complete()
	{
		_channel.Writer.TryComplete();
	}

	void OnStdout(byte[] data)
	{
		Append(LogOutput.Stdout, _stdoutDecoder, _stdoutPending, data);
	}

	void OnStderr(byte[] data)
	{
		Append(LogOutput.Stderr, _stderrDecoder, _stderrPending, data);
	}

	void Append(LogOutput stream, Decoder decoder, StringBuilder pending, byte[] data)
	{
		if (data.Length == 0)
		{
			return;
		}

		var chars = new char[decoder.GetCharCount(data, 0, data.Length, flush: false)];
		var count = decoder.GetChars(data, 0, data.Length, chars, 0, flush: false);
		lock (_sync)
		{
			pending.Append(chars, 0, count);
			FlushLines(stream, pending);
		}
	}

	void FlushLines(LogOutput stream, StringBuilder pending)
	{
		var text = pending.ToString();
		pending.Clear();
		var start = 0;
		for (var i = 0; i < text.Length; i++)
		{
			if (text[i] == '\n')
			{
				Add(new ContainerLogEntry(DateTimeOffset.UtcNow, stream, text[start..i].TrimEnd('\r')));
				start = i + 1;
			}
		}

		if (start < text.Length)
		{
			pending.Append(text[start..]);
		}
	}

	void FlushRemaining()
	{
		lock (_sync)
		{
			if (_stdoutPending.Length > 0)
			{
				Add(new ContainerLogEntry(DateTimeOffset.UtcNow, LogOutput.Stdout, _stdoutPending.ToString()));
				_stdoutPending.Clear();
			}

			if (_stderrPending.Length > 0)
			{
				Add(new ContainerLogEntry(DateTimeOffset.UtcNow, LogOutput.Stderr, _stderrPending.ToString()));
				_stderrPending.Clear();
			}
		}
	}

	void Add(ContainerLogEntry entry)
	{
		_entries.Add(entry);
		while (_entries.Count > MaxEntries)
		{
			_entries.RemoveAt(0);
		}

		_channel.Writer.TryWrite(entry);
	}
}
