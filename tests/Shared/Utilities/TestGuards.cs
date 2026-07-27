namespace VisualNotes.Testing.Utilities;

public static class TestGuards
{
    public static async Task CompletesWithin(this Task task, TimeSpan timeout) => await task.WaitAsync(timeout);

    public static void AssertNoTemporaryFiles(string root)
    {
        if (!Directory.Exists(root)) return;
        var leaked = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).ToArray();
        if (leaked.Length != 0) throw new InvalidOperationException($"Temporary resources leaked:{Environment.NewLine}{string.Join(Environment.NewLine, leaked)}");
    }
}
