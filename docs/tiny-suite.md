# The Tiny Suite

TinyDispatcher, TinyValidations, and TinyEvents are designed to work together as a small application layer.

They are not a platform. They are not an enterprise framework. They are a set of focused libraries for teams that want explicit application code, compile-time help, and boring runtime behavior.

## The Application Layer Shape

The intended flow is:

```text
request
  -> command/query
  -> validation
  -> dispatch
  -> use case / handler
  -> durable event publication
  -> outbox worker
  -> event consumer
```

Each library owns one part of that flow.

## TinyDispatcher

TinyDispatcher owns command and query execution.

It gives the application a clear entry point:

```csharp
await dispatcher.DispatchAsync(new RegisterUser(email), ct);
```

Handlers stay explicit. Pipelines are generated. Runtime dispatch does not depend on assembly scanning.

Use TinyDispatcher when you want application use cases to be discoverable, testable, and consistently executed.

## TinyValidations

TinyValidations owns application input validation.

It keeps validation close to the command:

```csharp
public sealed class RegisterUserValidation : IValidation<RegisterUser>
{
    public void Define(ValidationRules<RegisterUser> rules)
    {
        rules.Required(x => x.Email);
        rules.Email(x => x.Email);
    }
}
```

The source generator turns validation declarations into executable validators.

Use TinyValidations when you want validation to run before the handler and to stay visible in application code.

## TinyEvents

TinyEvents owns reliable application-event handling.

Use cases publish events:

```csharp
await events.PublishAsync(new UserRegistered(user.Id, user.Email), ct);
```

Consumers handle those events later:

```csharp
public sealed class SendWelcomeEmail : IEventConsumer<UserRegistered>
{
    public ValueTask ConsumeAsync(
        UserRegistered @event,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
```

The outbox message is the durable record of an event that still needs handling. The consumer is still the event handler. TinyEvents gives that local handler the reliability of the outbox pattern without requiring a broker, bus abstraction, or separate async messaging platform.

## How They Fit Together

A typical feature can look like this:

```text
RegisterUser command
  -> RegisterUserValidation
  -> RegisterUserHandler
  -> ITinyEventPublisher.PublishAsync(UserRegistered)
  -> TinyOutbox
  -> SendWelcomeEmail consumer
```

TinyDispatcher controls the application flow.

TinyValidations protects the handler from invalid input.

TinyEvents records reliable side effects after the use case accepts the event.

The result is an application layer with:

- explicit commands and queries
- validation before execution
- generated dispatch and validation infrastructure
- durable event handling
- no runtime assembly scanning
- no broker requirement for local reliable side effects
- small public contracts

## TheTinyApplicationLayer sample

The shared sample lives in the sibling `TheTinyApplicationLayer` repository.

It is an ASP.NET Core and Blazor application that uses the three TinySuite NuGet packages together:

```text
Blazor Form
-> API Endpoint
-> TinyValidations
-> TinyDispatcher
-> Use Case
-> TinyEvents Outbox
-> Worker
-> Event Consumer
```

TinyEvents appears after the use case accepts the command. The handler publishes an application event, TinyEvents stores it in the outbox with the same persistence boundary, and the worker later claims and processes the message.

Use the sample when you want to see TinyEvents working with TinyDispatcher and TinyValidations through real NuGet package references.

## Why Small Libraries

Successful open-source projects often grow because users need more integration, more support, more hosting choices, more cloud stories, and more commercial guarantees.

That success is good. But it can also turn a small library into a complex product owned by a company. The public API becomes larger. The dependency graph grows. The project starts serving every possible enterprise scenario. Eventually the simple thing that made the library attractive becomes hard to see.

The Tiny suite has a different bias.

The open-source libraries should stay small, readable, and useful on their own. Commercial software can be built around them, but the public OSS core should not become a disguised sales funnel or a heavy platform.

The philosophy is:

- keep the OSS contracts small
- keep runtime behavior boring
- use source generation only where it removes mechanical code
- avoid runtime scanning and magic
- avoid framework lock-in
- make commercial products compose around the libraries, not swallow them
- let users keep ownership of their architecture

## Commercial Software Around Public OSS

It is reasonable to build commercial software around open-source foundations.

The important line is ownership.

The public Tiny libraries should remain understandable and independently useful. Paid products, services, templates, hosting, support, or higher-level tooling can exist around them without making the core libraries worse.

That means the OSS layer should not need to absorb every commercial feature.

Examples of things that can stay outside the core:

- hosted dashboards
- operational tooling
- migration runners
- cloud deployment templates
- opinionated application templates
- paid support packages
- advanced integrations with specific platforms

The core stays small. The ecosystem can grow around it.

## What This Is Not

The Tiny suite is not trying to replace:

- ASP.NET Core
- EF Core
- message brokers
- workflow engines
- enterprise service buses
- full application frameworks

It is meant to sit inside ordinary .NET applications and make the application layer more explicit.

## The North Star

The Tiny suite should feel like a set of libraries a senior engineer would write for their own team:

- understandable in an afternoon
- safe enough for production use
- small enough to debug
- documented honestly
- friendly to tests
- respectful of the application's architecture

If a feature makes the public core harder to understand, it should probably live outside the core.
