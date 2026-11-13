using NetArchTest.Rules;
using Shouldly;

namespace Shop.ArchitectureTests;

public class ModuleBoundaryTests
{
    public static TheoryData<string> ModuleNames => Modules.Names();

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_DoesNotReferenceOtherModulesInternals(string name)
    {
        var m = Modules.Get(name);
        string[] forbidden = Modules.All
            .Where(o => o.Name != name)
            .SelectMany(o => new[] { $"{o.Name}.Domain", $"{o.Name}.Application", $"{o.Name}.Infrastructure" })
            .ToArray();

        var failures = new List<string>();
        foreach (var assembly in m.AllAssemblies)
        {
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
            if (!result.IsSuccessful)
                failures.AddRange(result.FailingTypeNames!.Select(t => $"{assembly.GetName().Name}: {t}"));
        }

        failures.ShouldBeEmpty("Offending types: " + string.Join(", ", failures));
    }
}
