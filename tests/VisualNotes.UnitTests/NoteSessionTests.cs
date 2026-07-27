using Shouldly;
using VisualNotes.Core.Models;
using Xunit;

namespace VisualNotes.UnitTests;

[Trait("Category", "Unit")]
public sealed class NoteSessionTests
{
    [Fact]
    public void NewSession_HasSafeDefaults()
    {
        var session = new NoteSession();

        session.Id.ShouldNotBe(Guid.Empty);
        session.IsPaused.ShouldBeFalse();
        session.Frames.ShouldBeEmpty();
        session.Sections.ShouldBeEmpty();
    }
}
