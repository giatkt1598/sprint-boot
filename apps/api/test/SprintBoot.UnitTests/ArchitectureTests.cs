using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace SprintBoot.UnitTests;

public class ArchitectureTests
{
    private static readonly string ModulesDirectory = Path.GetFullPath(
        Path.Combine(ThisDirectory(), "..", "..", "src", "Modules"));

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    private static IEnumerable<string> ProjectFiles() =>
        Directory.EnumerateFiles(ModulesDirectory, "*.csproj", SearchOption.AllDirectories);

    private static IEnumerable<string> ProjectReferences(string projectFile) =>
        XDocument.Load(projectFile).Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(
                reference.Attribute("Include")!.Value.Replace('\\', '/')));

    [Fact]
    public void Module_projects_are_discovered()
    {
        ProjectFiles().Count().ShouldBe(18);
    }

    [Fact]
    public void Modules_only_reference_contracts_of_other_modules()
    {
        foreach (var project in ProjectFiles())
        {
            var name = Path.GetFileNameWithoutExtension(project);
            foreach (var reference in ProjectReferences(project))
            {
                reference.EndsWith(".Contracts", StringComparison.Ordinal)
                    .ShouldBeTrue($"{name} must not reference {reference}, only .Contracts projects");
            }
        }
    }

    [Fact]
    public void Contracts_do_not_depend_on_implementation()
    {
        foreach (var project in ProjectFiles().Where(file => file.EndsWith(".Contracts.csproj", StringComparison.Ordinal)))
        {
            var packages = XDocument.Load(project).Descendants("PackageReference")
                .Select(package => package.Attribute("Include")!.Value);
            packages.ShouldNotContain(package => package.Contains("EntityFrameworkCore"), Path.GetFileName(project));
        }
    }
}
