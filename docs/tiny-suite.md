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

The Tiny suite keeps each library small, readable, and independently useful.

The philosophy is:

- keep the OSS contracts small
- keep runtime behavior boring
- use source generation only where it removes mechanical code
- avoid runtime scanning and magic
- avoid framework lock-in
- let users keep ownership of their architecture

## The North Star

The Tiny suite should feel like a set of libraries a senior engineer would write for their own team:

- understandable in an afternoon
- safe enough for production use
- small enough to debug
- documented honestly
- friendly to tests
- respectful of the application's architecture
