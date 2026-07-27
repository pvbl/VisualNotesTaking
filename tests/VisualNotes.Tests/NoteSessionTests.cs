using VisualNotes.Core.Models;

namespace VisualNotes.Tests;

public sealed class NoteSessionTests
{
    [Fact]
    public void NewSession_HasSafeDefaults()
    {
        var session = new NoteSession();

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.False(session.IsPaused);
        Assert.Empty(session.Frames);
        Assert.Empty(session.Sections);
    }
}
