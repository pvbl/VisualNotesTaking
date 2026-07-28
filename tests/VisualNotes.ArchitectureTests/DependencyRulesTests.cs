using System.Reflection;

using NetArchTest.Rules;

using Shouldly;

using Xunit;

namespace VisualNotes.ArchitectureTests;

[Trait("Category", "Architecture")]
public sealed class DependencyRulesTests
{
    private static readonly Assembly[] ProductAssemblies =
    [
        typeof(global::VisualNotes.Core.Models.Entity).Assembly,
        typeof(global::VisualNotes.Infrastructure.VisualNotesRuntime).Assembly,
        typeof(global::VisualNotes.App.ViewModels.ViewModelBase).Assembly
    ];

    private static readonly string[] InfrastructureOnlyNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Google.GenerativeAI",
        "Google.Cloud.AIPlatform",
        "OpenAI",
        "Azure.AI.OpenAI"
    ];

    [Fact]
    public void Core_has_no_framework_or_provider_dependencies()
    {
        var result = Types.InAssembly(typeof(global::VisualNotes.Core.Models.Entity).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "System.Windows",
                "Microsoft.EntityFrameworkCore",
                "DocumentFormat.OpenXml",
                "Google.GenerativeAI",
                "Google.Cloud.AIPlatform",
                "OpenAI",
                "Azure.AI.OpenAI",
                "VisualNotes.Infrastructure")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Application_view_models_depend_on_abstractions_not_infrastructure()
    {
        var result = Types.InAssembly(typeof(global::VisualNotes.App.ViewModels.ViewModelBase).Assembly)
            .That()
            .ResideInNamespace("VisualNotes.App.ViewModels")
            .ShouldNot()
            .HaveDependencyOnAny("VisualNotes.Infrastructure", "Microsoft.EntityFrameworkCore")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_application_layer()
    {
        var result = Types.InAssembly(typeof(global::VisualNotes.Infrastructure.VisualNotesRuntime).Assembly)
            .ShouldNot().HaveDependencyOn("VisualNotes.App").GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Provider_and_storage_sdks_are_confined_to_infrastructure()
    {
        var assemblies = new[]
        {
            typeof(global::VisualNotes.Core.Models.Entity).Assembly,
            typeof(global::VisualNotes.App.ViewModels.ViewModelBase).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(InfrastructureOnlyNamespaces)
                .GetResult();

            AssertSuccessful(result);
        }
    }

    [Theory]
    [InlineData("Handler")]
    [InlineData("Provider")]
    [InlineData("Repository")]
    public void Role_types_follow_their_naming_convention(string suffix)
    {
        var assemblies = new[]
        {
            typeof(global::VisualNotes.Core.Models.Entity).Assembly,
            typeof(global::VisualNotes.Infrastructure.VisualNotesRuntime).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var contracts = assembly.GetTypes().Where(type => type.IsInterface &&
                type.Name.EndsWith(suffix, StringComparison.Ordinal)).ToArray();
            foreach (var contract in contracts)
            {
                var result = Types.InAssemblies(ProductAssemblies).That().ImplementInterface(contract)
                    .Should().HaveNameEndingWith(suffix).GetResult();
                AssertSuccessful(result);
            }
        }
    }

    [Fact]
    public void View_models_use_the_ViewModel_suffix()
    {
        var result = Types.InAssembly(typeof(global::VisualNotes.App.ViewModels.ViewModelBase).Assembly)
            .That().ResideInNamespace("VisualNotes.App.ViewModels")
            .And().Inherit(typeof(global::VisualNotes.App.ViewModels.ViewModelBase))
            .Should().HaveNameEndingWith("ViewModel").GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Network_and_disk_async_boundaries_expose_cancellation()
    {
        var boundaryTypes = new[]
        {
            typeof(global::VisualNotes.Infrastructure.LanguageModels.OpenAiLanguageModelProvider),
            typeof(global::VisualNotes.Infrastructure.LanguageModels.GeminiLanguageModelProvider),
            typeof(global::VisualNotes.Infrastructure.JsonSettingsStore),
            typeof(global::VisualNotes.Infrastructure.Persistence.ImageFileStore),
            typeof(global::VisualNotes.Infrastructure.Persistence.ScreenshotStorageService),
            typeof(global::VisualNotes.Infrastructure.Persistence.SessionBackupService),
            typeof(global::VisualNotes.Infrastructure.Documents.OpenXmlDocumentExporter)
        };

        var offenders = boundaryTypes.SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => typeof(Task).IsAssignableFrom(method.ReturnType) ||
                             method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            .Where(method => method.GetParameters().All(parameter => parameter.ParameterType != typeof(CancellationToken)))
            .Select(method => $"{type.Name}.{method.Name}"))
            .ToArray();

        offenders.ShouldBeEmpty("Every public asynchronous network/disk operation must accept CancellationToken.");
    }

    private static void AssertSuccessful(TestResult result) =>
        result.IsSuccessful.ShouldBeTrue($"Forbidden dependencies: {string.Join(", ", result.FailingTypeNames ?? [])}");
}
