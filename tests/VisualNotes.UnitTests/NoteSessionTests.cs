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
        session.Screenshots.ShouldBeEmpty();
        session.Sections.ShouldBeEmpty();
    }

    [Fact]
    public void Sections_in_one_session_can_belong_to_different_academic_locations()
    {
        var session = new NoteSession();
        var firstCourse = new Course { Name = "Matemáticas" };
        var firstModule = new CourseModule { Course = firstCourse, CourseId = firstCourse.Id, Name = "Álgebra" };
        var secondCourse = new Course { Name = "Física" };
        var secondModule = new CourseModule { Course = secondCourse, CourseId = secondCourse.Id, Name = "Mecánica" };
        session.Sections.Add(new NoteSection
        {
            SessionId = session.Id, Title = "Matrices",
            Course = firstCourse, CourseId = firstCourse.Id,
            CourseModule = firstModule, CourseModuleId = firstModule.Id
        });
        session.Sections.Add(new NoteSection
        {
            SessionId = session.Id, Title = "Fuerzas",
            Course = secondCourse, CourseId = secondCourse.Id,
            CourseModule = secondModule, CourseModuleId = secondModule.Id
        });

        session.Sections.Select(section => section.Course!.Name)
            .ShouldBe(["Matemáticas", "Física"]);
    }
}
