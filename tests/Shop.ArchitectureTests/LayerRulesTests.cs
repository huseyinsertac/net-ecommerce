using System.Reflection;
using NetArchTest.Rules;
using Shouldly;
using Shop.Application.Abstractions.Events;
using Shop.Application.Abstractions.Messaging;

namespace Shop.ArchitectureTests;

public class LayerRulesTests
{
    private const string EfCore = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";

    public static TheoryData<string> ModuleNames => Modules.Names();

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        result.IsSuccessful.ShouldBeTrue(
            "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Domain_DoesNotDependOn_Application_Infrastructure_EfCore_AspNetCore(string name)
    {
        var m = Modules.Get(name);
        AssertNoDependency(m.Domain, $"{name}.Application", $"{name}.Infrastructure", EfCore, AspNetCore);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Application_DoesNotDependOn_Infrastructure_EfCore_AspNetCore(string name)
    {
        var m = Modules.Get(name);
        AssertNoDependency(m.Application, $"{name}.Infrastructure", EfCore, AspNetCore);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Contracts_DependOnlyOnSharedKernel(string name)
    {
        var m = Modules.Get(name);
        AssertNoDependency(m.Contracts, $"{name}.Domain", $"{name}.Application", $"{name}.Infrastructure", EfCore, AspNetCore);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void DomainEventHandlers_AndCommandHandlers_AreNotPublic(string name)
    {
        var m = Modules.Get(name);
        Type[] handlerInterfaces =
        [
            typeof(ICommandHandler<>), typeof(ICommandHandler<,>), typeof(IQueryHandler<,>),
            typeof(IDomainEventHandler<>), typeof(IIntegrationEventHandler<>),
        ];

        var offenders = m.Application.GetTypes()
            .Where(t => t is { IsClass: true, IsPublic: true } &&
                        t.GetInterfaces().Any(i => i.IsGenericType &&
                                                   handlerInterfaces.Contains(i.GetGenericTypeDefinition())))
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty("Public handlers: " + string.Join(", ", offenders));
    }
}
