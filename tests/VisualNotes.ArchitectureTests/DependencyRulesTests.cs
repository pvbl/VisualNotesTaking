using NetArchTest.Rules;
using Xunit;

namespace VisualNotes.ArchitectureTests;

public sealed class DependencyRulesTests
{
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

    private static void AssertSuccessful(TestResult result) =>
        Assert.True(result.IsSuccessful, $"Forbidden dependencies: {string.Join(", ", result.FailingTypeNames ?? [])}");
}
