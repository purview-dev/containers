namespace Purview.Containers.Wsl;

public class WslSessionSweeperTests
{
	const string SessionList = """
		ID        Creator PID   Display Name
		1         28116         wslc-cli-Kieron
		21        64548         purview-64548-66f3e104
		22        64549         wslc-64549-0badc0de
		23        64550         other-session
		24        64551         purview-64551-ABCDEF01
		25        64552         purview-64552-1234
		""";

	[Test]
	[Arguments("purview-123-abcdef12", 123, true)]
	[Arguments("wslc-123-abcdef12", 123, true)]
	[Arguments("purview-1-00000000", 1, true)]
	[Arguments("wslc-cli-Kieron", 0, false)]
	[Arguments("purview-123-ABCDEF12", 0, false)]
	[Arguments("purview-123-abcdef1", 0, false)]
	[Arguments("purview-x-abcdef12", 0, false)]
	[Arguments("other-123-abcdef12", 0, false)]
	[Arguments("", 0, false)]
	public async Task TryParseSessionName_OnlyAcceptsLibraryNames(string name, int expectedProcessId, bool expected)
	{
		var parsed = WslSessionSweeper.TryParseSessionName(name, out var processId);

		await Assert.That(parsed).IsEqualTo(expected);
		await Assert.That(processId).IsEqualTo(expected ? expectedProcessId : 0);
	}

	[Test]
	public async Task ParseSessions_IgnoresHeaderCLIAndForeignSessions()
	{
		var sessions = WslSessionSweeper.ParseSessions(SessionList);

		await Assert
			.That(sessions.Select(session => session.Name))
			.IsEquivalentTo(["purview-64548-66f3e104", "wslc-64549-0badc0de"]);
		await Assert.That(sessions.Select(session => session.CreatorProcessId)).IsEquivalentTo([64548, 64549]);
	}

	[Test]
	public async Task RemoveOrphanedStorageDirectories_KeepsLiveAndUnrelatedDirectories()
	{
		var root = Path.Combine(Path.GetTempPath(), "purview-sweep-" + Guid.NewGuid().ToString("N")[..8]);
		Directory.CreateDirectory(root);
		try
		{
			var current = Environment.ProcessId;
			var dead = Path.Combine(root, "purview-999999-abcdef12");
			var legacyDead = Path.Combine(root, "wslc-999997-abcdef12");
			var live = Path.Combine(root, $"purview-{current}-abcdef12");
			var foreign = Path.Combine(root, "other-session");
			var failed = Path.Combine(root, "purview-999998-abcdef12");
			foreach (var directory in new[] { dead, legacyDead, live, foreign, failed })
			{
				Directory.CreateDirectory(directory);
			}

			var removed = WslSessionSweeper.RemoveOrphanedStorageDirectories(
				current,
				listedSessionNames: new HashSet<string>(["purview-999998-abcdef12"], StringComparer.Ordinal),
				terminatedSessionNames: [],
				root: root
			);

			await Assert.That(removed).IsEquivalentTo(["purview-999999-abcdef12", "wslc-999997-abcdef12"]);
			await Assert.That(Directory.Exists(dead)).IsFalse();
			await Assert.That(Directory.Exists(legacyDead)).IsFalse();
			await Assert.That(Directory.Exists(live)).IsTrue();
			await Assert.That(Directory.Exists(foreign)).IsTrue();
			await Assert.That(Directory.Exists(failed)).IsTrue();
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}
}
