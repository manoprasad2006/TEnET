using System.Text.RegularExpressions;

namespace TenNet.Tests;

public sealed class ArchitectureTests
{
    private static readonly string[] ForbiddenApis =
    [
        "IgnoreQueryFilters",
        "FromSqlRaw",
        "ExecuteSqlRaw",
        "ExecuteUpdate",
        "ExecuteDelete"
    ];

    [Fact]
    public void ProductionCodeMustNotUseForbiddenEfBypassApis()
    {
        var solutionDir = FindSolutionDirectory();
        var projectsToScan = new[] { "Domain", "Data", "Api" };

        var violations = new List<string>();

        foreach (var project in projectsToScan)
        {
            var projectDir = Path.Combine(solutionDir, project);
            if (!Directory.Exists(projectDir)) continue;

            var csFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

            foreach (var file in csFiles)
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    // Skip comments
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                        continue;

                    foreach (var forbidden in ForbiddenApis)
                    {
                        var pattern = $@"\b{forbidden}\s*(\(|<)";
                        if (Regex.IsMatch(line, pattern))
                        {
                            violations.Add($"{Path.GetRelativePath(solutionDir, file)}:{i + 1} uses forbidden API '{forbidden}'");
                        }
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Architecture violation: Forbidden EF query filter bypass or raw SQL APIs found outside allowlist:\n{string.Join("\n", violations)}");
    }

    private static string FindSolutionDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TenNet.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate TenNet.sln directory.");
    }
}
