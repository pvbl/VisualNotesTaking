using NSubstitute;

using Shouldly;

using VisualNotes.App.ViewModels;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UiTests;

public sealed class SessionAndSettingsUiTests
{
    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public async Task Restored_session_is_selected_and_can_create_a_root_section()
    {
        var session = new NoteSession { Name = "Restaurada" };
        var sessions = Substitute.For<ISessionRepository>();
        var screenshots = Substitute.For<IScreenshotRepository>();
        var settings = Substitute.For<ISettingsRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        sessions.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<NoteSession>>([session]));
        sessions.GetAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<NoteSession?>(session));
        settings.GetAsync<Guid?>(SessionCoordinator.LastOpenSessionKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(session.Id));
        var main = new MainViewModel(new(sessions, screenshots, settings, unitOfWork), sessions);

        await main.InitializeAsync();
        await ((AsyncRelayCommand)main.Sessions.AddSectionCommand).ExecuteAsync();

        main.Sessions.SelectedSession.ShouldBeSameAs(session);
        main.ActiveSession.ShouldBeSameAs(session);
        session.Sections.Count.ShouldBe(1);
        main.Sessions.SelectedSection.ShouldBe(session.Sections.Single());
        main.Sessions.SelectedSection.ShouldNotBeNull().ParentSectionId.ShouldBeNull();
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Api_key_editor_is_visible_first_and_uses_a_responsive_layout()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "VisualNotes.App");
        var settings = File.ReadAllText(Path.GetFullPath(Path.Combine(root, "Views", "SettingsView.xaml")));
        var shell = File.ReadAllText(Path.GetFullPath(Path.Combine(root, "MainWindow.xaml")));

        settings.ShouldContain("AutomationProperties.AutomationId=\"ApiCredentialsSection\"");
        settings.ShouldContain("Text=\"API key de OpenAI\"");
        settings.ShouldContain("AutomationProperties.Name=\"Nueva API key\"");
        settings.IndexOf("API key de OpenAI", StringComparison.Ordinal)
            .ShouldBeLessThan(settings.IndexOf("Configuración por niveles", StringComparison.Ordinal));
        shell.ShouldContain("Content=\"Configuración y API\"");
    }
}
