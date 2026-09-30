using System.Text;
using System.Threading.Channels;
using Microsoft.WSL.Containers;

namespace Purview.WslContainers;

/// <summary>
/// Bounded buffer for init-process output. Accumulates decoded lines for diagnostics while publishing
/// every entry to a channel so wait strategies and consumers can tail live output.
/// </summary>
internal sealed class LogBuffer
{
	private const int MaxEntries = 4096;
	private readonly object sync = new();
	private readonly List<ContainerLogEntry> entries = new(MaxEntries);
	private readonly Channel<ContainerLogEntry> channel = Channel.CreateBounded<ContainerLogEntry>(
		new BoundedChannelOptions(1024)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
			SingleWriter = false,
		}
	);
	private readonly Decoder stdoutDecoder = Encoding.UTF8.GetDecoder();
	private readonly Decoder stderrDecoder = Encoding.UTF8.GetDecoder();
	private readonly StringBuilder stdoutPending = new();
	private readonly StringBuilder stderrPending = new();
	private int attached;

	public void Attach(Process process)
	{
		if (Interlocked.Exchange(ref attached, 1) == 1)
		{
			return;
		}

		process.OutputReceived += OnStdout;
		process.ErrorReceived += OnStderr;
		process.Exited += _ => FlushRemaining();
	}

	public IReadOnlyList<ContainerLogEntry> Snapshot()
	{
		lock (sync)
		{
			return entries.ToArray();
		}
	}

	public string GetLogs(LogOutput? stream)
	{
		lock (sync)
		{
			IEnumerable<ContainerLogEntry> source = stream is LogOutput s
				? entries.Where(entry => entry.Stream == s)
				: entries;
			return string.Join('\n', source.Select(entry => entry.Text));
		}
	}

	public ChannelReader<ContainerLogEntry> Reader => channel.Reader;

	private void OnStdout(byte[] data)
	{
		Append(LogOutput.Stdout, stdoutDecoder, stdoutPending, data);
	}

	private void OnStderr(byte[] data)
	{
		Append(LogOutput.Stderr, stderrDecoder, stderrPending, data);
	}

	private void Append(LogOutput stream, Decoder decoder, StringBuilder pending, byte[] data)
	{
		if (data.Length == 0)
		{
			return;
		}

		char[] chars = new char[decoder.GetCharCount(data, 0, data.Length, flush: false)];
		int count = decoder.GetChars(data, 0, data.Length, chars, 0, flush: false);
		lock (sync)
		{
			pending.Append(chars, 0, count);
			FlushLines(stream, pending);
		}
	}

	private void FlushLines(LogOutput stream, StringBuilder pending)
	{
		string text = pending.ToString();
		pending.Clear();
		int start = 0;
		for (int i = 0; i < text.Length; i++)
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

	private void FlushRemaining()
	{
		lock (sync)
		{
			if (stdoutPending.Length > 0)
			{
				Add(new ContainerLogEntry(DateTimeOffset.UtcNow, LogOutput.Stdout, stdoutPending.ToString()));
				stdoutPending.Clear();
			}

			if (stderrPending.Length > 0)
			{
				Add(new ContainerLogEntry(DateTimeOffset.UtcNow, LogOutput.Stderr, stderrPending.ToString()));
				stderrPending.Clear();
			}
		}
	}

	private void Add(ContainerLogEntry entry)
	{
		entries.Add(entry);
		while (entries.Count > MaxEntries)
		{
			entries.RemoveAt(0);
		}

		channel.Writer.TryWrite(entry);
	}
}
