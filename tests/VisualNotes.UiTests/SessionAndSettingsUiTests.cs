using NSubstitute;

using Shouldly;

using VisualNotes.App.ViewModels;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UiTests;

public sealed class SessionAndSettingsUiTests
{
    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public async Task Startup_leaves_old_sessions_unselected_and_first_section_creates_a_new_session()
    {
        var oldSession = new NoteSession { Name = "Anterior" };
        var sessions = Substitute.For<ISessionRepository>();
        var screenshots = Substitute.For<IScreenshotRepository>();
        var settings = Substitute.For<ISettingsRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        sessions.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<NoteSession>>([oldSession]));
        var main = new MainViewModel(new(sessions, screenshots, settings, unitOfWork), sessions);

        await main.InitializeAsync();
        main.Sessions.SelectedSession.ShouldBeNull();
        main.ActiveSession.ShouldBeNull();
        main.Sessions.Draft.Name = "Álgebra lineal";
        main.Sessions.Draft.Module = "Matrices";
        await ((AsyncRelayCommand)main.Sessions.AddSectionCommand).ExecuteAsync();

        var created = main.Sessions.SelectedSession.ShouldNotBeNull();
        created.ShouldNotBeSameAs(oldSession);
        created.Name.ShouldMatch(@"^\d{4}-\d{2}-\d{2}_Álgebra_lineal_Matrices$");
        main.ActiveSession.ShouldBeSameAs(created);
        created.Sections.Count.ShouldBe(1);
        main.Sessions.SelectedSection.ShouldBe(created.Sections.Single());
        main.Sessions.SelectedSection.ShouldNotBeNull().ParentSectionId.ShouldBeNull();
        main.Sessions.LastActionMessage.ShouldContain(created.Name);
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public void Automatic_session_name_is_sanitized_and_deduplicated()
    {
        var draft = new NoteSession { Name = "Cálculo / avanzado", Module = "Módulo 1" };

        var result = SessionViewModel.BuildAutomaticSessionName(draft,
            new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero),
            ["2026-07-28_Cálculo_avanzado_Módulo_1"]);

        result.ShouldBe("2026-07-28_Cálculo_avanzado_Módulo_1_2");
    }

    [Fact, Trait("Category", "UI"), Trait("Category", "Windows")]
    public async Task First_capture_gets_an_automatic_session_and_a_general_section()
    {
        var sessions = Substitute.For<ISessionRepository>();
        sessions.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<NoteSession>>([]));
        var coordinator = new SessionCoordinator(sessions, Substitute.For<IScreenshotRepository>(),
            Substitute.For<ISettingsRepository>(), Substitute.For<IUnitOfWork>());
        var main = new MainViewModel(coordinator, sessions);
        await main.InitializeAsync();

        var created = await main.EnsureActiveSessionAsync();

        created.Name.ShouldMatch(@"^\d{4}-\d{2}-\d{2}_Sesión$");
        created.Sections.Count.ShouldBe(1);
        created.Sections.Single().Title.ShouldBe("General");
        created.ActiveSectionId.ShouldBe(created.Sections.Single().Id);
        main.Sessions.SelectedSection.ShouldBe(created.Sections.Single());
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
