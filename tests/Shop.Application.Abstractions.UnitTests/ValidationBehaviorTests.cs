using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shop.Application.Abstractions.Behaviors;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;
using Shouldly;

namespace Shop.Application.Abstractions.UnitTests;

public sealed record CreateThing(string Name) : ICommand;

public sealed class CreateThingValidator : AbstractValidator<CreateThing>
{
    public CreateThingValidator() => RuleFor(x => x.Name).NotEmpty();
}

public sealed class TestLog
{
    public List<string> Entries { get; } = [];
    public int HandlerCalls { get; set; }
}

public sealed class CreateThingHandler(TestLog log) : ICommandHandler<CreateThing>
{
    public Task<Result> Handle(CreateThing command, CancellationToken ct)
    {
        log.HandlerCalls++;
        log.Entries.Add("handler");
        return Task.FromResult(Result.Success());
    }
}

public sealed class RecordingBehavior(TestLog log) : IPipelineBehavior<CreateThing, Result>
{
    public Task<Result> Handle(CreateThing request, HandlerDelegate<Result> next, CancellationToken ct)
    {
        log.Entries.Add("test-behavior");
        return next();
    }
}

public sealed class RecordingLoggerProvider(TestLog log) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new RecordingLogger(log);
    public void Dispose() { }

    private sealed class RecordingLogger(TestLog log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => log.Entries.Add("logging");
    }
}

public class ValidationBehaviorTests
{
    private static (ServiceProvider Sp, TestLog Log) Build()
    {
        var log = new TestLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddLogging(b => b.AddProvider(new RecordingLoggerProvider(log)));
        services.AddDispatcher();
        services.AddApplicationHandlers(typeof(ValidationBehaviorTests).Assembly);
        services.AddScoped<IPipelineBehavior<CreateThing, Result>, RecordingBehavior>();
        return (services.BuildServiceProvider(), log);
    }

    [Fact]
    public async Task Validation_InvalidCommand_ReturnsValidationError_HandlerNotCalled()
    {
        var (sp, log) = Build();
        using (sp)
        {
            var result = await sp.GetRequiredService<IDispatcher>().Send(new CreateThing(""), TestContext.Current.CancellationToken);
            result.IsFailure.ShouldBeTrue();
            var error = result.Error.ShouldBeOfType<ValidationError>();
            error.Errors["Name"].ShouldNotBeEmpty();
            log.HandlerCalls.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Validation_ValidCommand_ReachesHandler()
    {
        var (sp, log) = Build();
        using (sp)
        {
            var result = await sp.GetRequiredService<IDispatcher>().Send(new CreateThing("x"), TestContext.Current.CancellationToken);
            result.IsSuccess.ShouldBeTrue();
            log.HandlerCalls.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Behaviors_RunInOrder_LoggingThenValidationThenHandler()
    {
        var (sp, log) = Build();
        using (sp)
        {
            var dispatcher = sp.GetRequiredService<IDispatcher>();

            // Invalid: validation (outside the test behavior) short-circuits; logging (outermost) still reports.
            await dispatcher.Send(new CreateThing(""), TestContext.Current.CancellationToken);
            log.Entries.ShouldNotContain("test-behavior");
            log.Entries.ShouldNotContain("handler");
            log.Entries.ShouldContain("logging");

            // Valid: validation -> test behavior -> handler, with logging wrapping all of it (logs on completion).
            log.Entries.Clear();
            await dispatcher.Send(new CreateThing("ok"), TestContext.Current.CancellationToken);
            log.Entries.ShouldBe(["test-behavior", "handler", "logging"]);
        }
    }
}
