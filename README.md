# alaasmagi-dotnet-template

A .NET 10 backend solution template for Alaasmagi services. The template is shaped after the existing
`email-hub` and `invoice-service` backends and uses the `alaasmagi.Base.*` packages as the default
foundation for domain entities, DTO mapping, EF repositories, application services, Keycloak
authentication, Redis caching, and RabbitMQ messaging.

The template includes one real cross-project entity, `AppUser`, because every service needs a local
projection of the authenticated Keycloak user.

## Features

- Clean layered solution split into `Domain`, `DTO`, `Contracts`, `DataAccess`, `Application`,
  `External`, `Web`, and `Tests` (NUnit).
- `AppUser` domain model, EF entity, web DTO, bidirectional mappers, repository, application service,
  and read-only API controller.
- Keycloak OpenID Connect (MVC) and JWT bearer (API) authentication through `alaasmagi.Base.Keycloak`,
  with permission-based authorization through `alaasmagi.Base.Authorization`.
- RabbitMQ publishing and consuming through `alaasmagi.Base.Message.RabbitMQ`.
- Redis-ready caching through `alaasmagi.Base.Cache`.
- Keycloak user lifecycle event consumer that keeps the local `AppUsers` table synchronized, with
  idempotent handling through the `processed_messages` table.
- MVC shell with login/logout and health endpoint.
- API endpoints under `/api`.
- Health endpoint at `/health`.
- Swagger UI at `/swagger`.
- URL-segment API versioning with `/api/v1/...` routes.
- Fixed-window API rate limiting.
- PostgreSQL EF Core context with design-time factory for migrations.
- Environment-driven configuration loaded from `.env`.
- Complete `.env.example` listing every variable for database, Redis, Keycloak, RabbitMQ, Glitchtip, and ports.
- Central build settings in `Directory.Build.props`.

## Technology

- .NET 10 / C# latest
- ASP.NET Core MVC and API controllers
- Entity Framework Core with PostgreSQL
- Keycloak OIDC authentication
- Redis caching
- RabbitMQ topic-exchange messaging
- Sentry/Glitchtip-compatible error reporting
- `alaasmagi.Base.*` version `1.8.0`

## Folder Tree

```text
.
|-- Application
|   |-- AppUserService.cs
|   `-- ProcessedMessageService.cs
|-- Contracts
|   |-- Application
|   |   |-- IAppUserService.cs
|   |   `-- IProcessedMessageService.cs
|   |-- DataAccess
|   |   |-- IAppUserRepository.cs
|   |   `-- IProcessedMessageRepository.cs
|   `-- External
|       |-- IAppCache.cs
|       `-- IAppEventPublisher.cs
|-- DTO
|   |-- DataAccess
|   |   |-- AppUserEntity.cs
|   |   |-- ProcessedMessageEntity.cs
|   |   `-- Mappers
|   |       `-- AppUserEntityMapper.cs
|   `-- Web
|       |-- AppUserDto.cs
|       `-- Mappers
|           `-- AppUserDtoMapper.cs
|-- DataAccess
|   |-- AppUserRepository.cs
|   |-- DataAccessUow.cs
|   |-- ProcessedMessageRepository.cs
|   `-- Context
|       |-- AppDbContext.cs
|       `-- AppDbContextFactory.cs
|-- Domain
|   `-- AppUser.cs
|-- External
|   |-- Cache
|   |   `-- RedisCache.cs
|   `-- RabbitMQ
|       |-- AppMessagingOptions.cs
|       |-- RabbitMqEventConsumer.cs
|       |-- RabbitMqEventHandler.cs
|       `-- RabbitMqEventPublisher.cs
|-- Web
|   |-- API
|   |   `-- Controllers
|   |       `-- AppUsersController.cs
|   |-- Configuration
|   |   |-- ApiRateLimitOptions.cs
|   |   |-- DotEnvConfiguration.cs
|   |   |-- GlitchtipConfiguration.cs
|   |   |-- LoggingConfiguration.cs
|   |   |-- ProcessedMessageCleanupService.cs
|   |   |-- RateLimitPolicies.cs
|   |   |-- RequiredConfiguration.cs
|   |   `-- ServiceConfiguration.cs
|   |-- MVC
|   |   |-- Controllers
|   |   |-- Models
|   |   `-- Views
|   `-- Program.cs
|-- Tests
|   |-- AppUserServiceTests.cs
|   |-- AppUserTests.cs
|   |-- MapperTests.cs
|   `-- MessagingTests.cs
|-- .env.example
|-- Directory.Build.props
`-- alaasmagi-dotnet-template.sln
```

## Project Responsibilities

### `Domain`

Contains business/domain models. `AppUser` inherits `BaseEntityWithConcurrency` from
`alaasmagi.Base.Domain`, so the entity has:

- `Id`
- `ConcurrencyToken`

The persistence entity `AppUserEntity` inherits `BaseEntityWithMetaConcurrency` and also stores
`CreatedAt` / `CreatedBy` and `UpdatedAt` / `UpdatedBy`.

Use this project for domain concepts and behavior that should not depend on EF, ASP.NET Core, or
transport infrastructure.

### `DTO`

Contains boundary models and mappers.

- `DTO/DataAccess` contains EF persistence entities.
- `DTO/DataAccess/Mappers` maps domain models to EF entities.
- `DTO/Web` contains API/MVC DTOs.
- `DTO/Web/Mappers` maps web DTOs to domain models.

The mapper contract is `IMapper<TUpper, TLower>` from `alaasmagi.Base.Contracts.DTO`.

`AppUserEntity` keeps its persistence constraints as data annotations, by convention:

- `[Required]`
- `[MaxLength(...)]`

`AppDbContext` keeps schema, table, index, and relationship configuration.

### `Contracts`

Contains interfaces only.

- `IAppUserRepository` extends `IBaseRepository<AppUser>`.
- `IAppUserService` extends `IBaseService<AppUser>`.
- `IProcessedMessageRepository` / `IProcessedMessageService` handle the processed message ids.
- `IAppCache` extends `IBaseCache`.
- `IAppEventPublisher` abstracts event publishing.

This preserves the dependency direction: callers depend on contracts, not implementations.

### `DataAccess`

Contains EF Core implementation.

- `AppDbContext` defines the database context.
- `AppDbContextFactory` supports EF Core design-time commands.
- `AppUserRepository` inherits `BaseRepository<AppUser, AppUserEntity, IMapper<AppUser, AppUserEntity>>`
  from `alaasmagi.Base.DataAccess.EF` and adds no custom queries.
- `DataAccessUow` inherits `BaseUow<AppDbContext>`.
- `ProcessedMessageRepository` stages and queries rows of the `processed_messages` table
  (`message_id uuid PK`, `processed_at timestamptz`, `status text null`) from the event contract.

The Base repository stages changes on the DbContext and sets metadata and concurrency tokens. It never
saves; the Unit of Work commits.

### `Application`

Contains application service implementations.

`AppUserService` inherits `BaseService<AppUser, AppUser, IAppUserRepository>` from
`alaasmagi.Base.Application`. The application and domain model are the same type here, so the service
uses an identity mapper.

The Base service handles the usual CRUD flow:

- maps application model to domain model
- calls the repository
- saves through `IBaseUow` on successful writes
- re-reads persisted rows after writes
- returns `IMethodResponse<T>` instead of throwing for expected failures

`AppUserService` adds `SyncFromIdentityAsync` (create or update the local user from a Keycloak event)
and `RemoveFromIdentityAsync`. Both take the message `id` and stage it in `processed_messages` together
with the `AppUser` change, so both are committed in one transaction with
`IBaseUow.TrySaveChangesAsync`. An already processed `id` returns `false` without changing anything;
rejected writes come back as a failed `IMethodResponse<T>`. Normalization of identity data (trimming,
fallback locale `et-EE` and time zone `Europe/Tallinn`) lives on the domain entity in
`AppUser.FromIdentity`.

`ProcessedMessageService` deletes processed message ids older than 7 days. `ProcessedMessageCleanupService`
in `Web/Configuration` runs it once a day.

### `External`

Contains infrastructure adapters for systems outside this service.

Redis cache support is built on `alaasmagi.Base.Contracts.Cache`, `alaasmagi.Base.Cache`, and
`StackExchange.Redis`.

- `IAppCache` is the service-level cache abstraction in `Contracts/External`.
- `RedisCache` inherits `BaseCache` and implements Redis byte operations.
- `BaseJsonCacheSerializer` handles typed value serialization.
- `BaseCacheKeyBuilder` handles key validation and prefixing.
- `BaseCacheOptions` configures the cache key prefix and default entry expiration.

RabbitMQ support is built on `alaasmagi.Base.Message` and `alaasmagi.Base.Message.RabbitMQ`.

- `RabbitMqEventPublisher` implements `IAppEventPublisher` and publishes Base event envelopes. The app slug
  (`APP_SLUG`) is both `source` and `tenant`, so the routing key is `<slug>.<slug>.<action>`.
  Actions are single hyphenated segments (e.g. `invoice-created`). Content deriving from
  `BaseNotificationContent` is validated with `Validate()` before publishing. Broker failures are
  returned as a failed `IMethodResponse<bool>` with code `PUBLISH_FAILED`.
- `RabbitMqEventConsumer` inherits `RabbitMqDeliveryConsumerBase<JsonElement>` and consumes the queue named
  by `RABBITMQ_QUEUE`. Queue bindings are set up manually, not in code.
- `RabbitMqEventHandler` handles incoming events and returns the delivery disposition. Keycloak user
  lifecycle events (source `identity-hub`, actions `user-created`, `user-updated`, `user-enabled`,
  `user-disabled`, `user-deleted`, `contentVersion` `1.0`) are read as `UserEventContent` from
  `alaasmagi.Base.Keycloak.Events` and passed to `IAppUserService` with the envelope `id`.
  - `Ack`: processed, already processed (duplicate `id`), unsupported identity action, or another source.
  - `Requeue`: transient database failure (a `DbException` with `IsTransient`, e.g. connection or timeout).
    The base class waits `RequeueDelay` (default 5 s) before requeueing.
  - `Reject`: invalid id or content, unknown `contentVersion`, or any other failure.

### `Web`

Composition root and delivery layer.

- `Program.cs` only loads `.env`, calls the `Configuration` extensions and wires the request pipeline.
- `Configuration` loads env vars and registers all services, including API versioning and rate limiting.
- `MVC` contains the default web shell and authentication UI.
- `API` contains JSON API controllers.

`AppUser` is a projection of the Keycloak user (`Id` is the JWT `sub`) and is written only from identity
events, so the API is read-only:

```text
GET /api/v1/app-users        requires app-users.view-all
GET /api/v1/app-users/{id}   the caller's own id, otherwise requires app-users.view-all
```

API controllers authenticate with Keycloak JWT bearer tokens only and return `401` / `403`. MVC pages use
Keycloak OpenID Connect with a cookie session. Authorization checks permissions only (`[RequirePermission]`,
`ICurrentUser.HasPermission`), never role names.

Operational endpoints:

```text
GET /health
GET /swagger
```

## `alaasmagi.Base.*` Packages

This template intentionally relies on Base packages rather than re-implementing common backend
plumbing.

| Package | Used for |
| --- | --- |
| `alaasmagi.Base.Domain` | Entity base classes such as `BaseEntityWithMetaConcurrency`. |
| `alaasmagi.Base.DTO` | Result/error implementations and DTO helpers. |
| `alaasmagi.Base.Contracts.Application` | `IBaseService<T>` service contract. |
| `alaasmagi.Base.Contracts.Cache` | `IBaseCache`, cache result, serializer, key builder, and entry option contracts. |
| `alaasmagi.Base.Contracts.DataAccess` | `IBaseRepository<T>` and `IBaseUow` (`SaveChangesAsync`, and `TrySaveChangesAsync` which returns rejected writes as a failed `IMethodResponse<int>`). |
| `alaasmagi.Base.DataAccess.EF` | EF Core `BaseRepository` and `BaseUow`. |
| `alaasmagi.Base.Application` | `BaseService` CRUD application-service implementation. |
| `alaasmagi.Base.Cache` | `BaseCache`, JSON serializer, key builder, cache result, and cache options. |
| `alaasmagi.Base.Message` | Event envelope model and default message constants. |
| `alaasmagi.Base.Message.RabbitMQ` | RabbitMQ publisher, consumer base classes (`RabbitMqConsumerBase<TContent>` for envelopes, `RabbitMqDeliveryConsumerBase` for raw deliveries with explicit ack/requeue/reject), and DI helpers. |
| `alaasmagi.Base.Keycloak` | Keycloak OIDC auth (configurable scopes, cookie paths, claim types and PAR), JWT bearer scheme, permission claim mapping, and identity event content DTOs. |
| `alaasmagi.Base.Authorization` | `[RequirePermission]`, `ICurrentUser`, `Actions` and the permission policy provider (referenced through `alaasmagi.Base.Keycloak`). |

Key Base conventions used here:

- Expected failures return `IMethodResponse<T>`.
- Repositories do not save changes directly for normal CRUD.
- Services coordinate repository calls and Unit of Work commits.
- Optimistic concurrency is enabled by entities implementing the Base concurrency contract.
- Cache access goes through string-keyed `IBaseCache` abstractions.
- Messaging uses the fixed envelope shape `{ id, source, tenant, action, timestamp, contentVersion, content }`.

## Configuration

Copy `.env.example` to `.env` and fill in the values. The code has no built-in defaults; a missing
required value fails at startup.

Required runtime values:

```text
DATABASE_CONNECTION_STRING
REDIS_CONNECTION_STRING
CACHE_KEY_PREFIX
CACHE_DEFAULT_ABSOLUTE_EXPIRATION_SECONDS
RABBITMQ_URI
RABBITMQ_QUEUE
RABBITMQ_EXCHANGE
APP_SLUG
RATE_LIMIT_PERMIT_LIMIT
RATE_LIMIT_WINDOW_SECONDS
RATE_LIMIT_QUEUE_LIMIT
KEYCLOAK_AUTHORITY
KEYCLOAK_CLIENT_ID
KEYCLOAK_CLIENT_SECRET
```

Optional values:

```text
APP_PORT
PORT
ASPNETCORE_URLS
HOST_PORT
GLITCHTIP_DSN          (empty disables Glitchtip)
GLITCHTIP_RELEASE      (required when GLITCHTIP_DSN is set)
```

`DotEnvConfiguration` searches upward from the current working directory for `.env` and loads values
that are not already present in the process environment.

## Architectural Decisions

### Clean layering

Each project owns one kind of responsibility. Domain does not depend on persistence or web concerns.
Contracts keep dependency direction stable. Implementations live in `Application`, `DataAccess`,
`External`, and `Web`.

### AppUser as the template entity

Every backend needs a local user projection for ownership, display, preferences, and user-scoped data.
`AppUser` is the default model in this template and is synchronized from Keycloak user lifecycle events.

### BaseService and BaseRepository for CRUD

Generic CRUD behavior comes from `alaasmagi.Base.Application` and `alaasmagi.Base.DataAccess.EF`.
New CRUD resources should follow the same pattern:

1. Domain model in `Domain`.
2. Persistence entity in `DTO/DataAccess`.
3. Web DTO in `DTO/Web`.
4. Domain-to-EF mapper in `DTO/DataAccess/Mappers`.
5. Web-to-domain mapper in `DTO/Web/Mappers`.
6. Repository interface in `Contracts/DataAccess`.
7. Service interface in `Contracts/Application`.
8. Repository implementation in `DataAccess`.
9. Service implementation in `Application`.
10. API controller in `Web/API/Controllers`.

### Data annotations for persistence constraints

Persistence DTO constraints such as required fields and max lengths are placed on the entity class with
data annotations. `AppDbContext` is reserved for schema/table/index/relationship configuration.

### Environment variables over appsettings for secrets

Secrets and deployment-specific values are read from environment variables. `appsettings.json` should
remain non-secret and generic.

### RabbitMQ topology is configuration-driven

The service reads RabbitMQ host/credentials/exchange/queue from env vars. Exchanges, queues and bindings
are created manually, never in code. Event payloads use the Base fixed envelope, so only `content`
varies per event type.

### RabbitMQ setup

Topology follows the platform event contract (`docs/contracts/event-contract.md` in `alaasmagi/base-nuget`)
and is created manually. For an app with slug `{slug}` (`APP_SLUG`):

| Item | Setting |
| --- | --- |
| Consumer queue | Durable quorum queue named `{slug}` (`RABBITMQ_QUEUE`) |
| Queue arguments | `x-queue-type: quorum`, `x-delivery-limit: 5`, `x-dead-letter-exchange: dead-letter`, `x-dead-letter-routing-key: {slug}` |
| Dead-letter exchange | `dead-letter`, durable `direct` exchange shared by all apps |
| Dead-letter queue | Durable quorum queue `{slug}.dlq`, bound to `dead-letter` with routing key `{slug}` |
| Identity events | Bind `{slug}` to `identity-hub.events` with `identity-hub.{slug}.#` |
| ai-hub replies (when used) | Bind `{slug}` to `ai-hub.events` with `ai-hub.{slug}.#` |
| Publishing | `RABBITMQ_EXCHANGE` is the one exchange the app publishes to, e.g. `notification-hub.commands` |

Messages in `{slug}.dlq` are inspected and moved back manually; nothing is replayed automatically.

### Keycloak setup

Authentication follows the platform JWT contract (`docs/contracts/jwt-contract.md` in `alaasmagi/base-nuget`).

1. Create a confidential client for this backend. Its client id is `KEYCLOAK_CLIENT_ID`, used both as the
   OIDC client of the MVC pages and as the expected token audience of the API; its secret is
   `KEYCLOAK_CLIENT_SECRET`. Add the MVC callback URLs as valid redirect URIs.
2. Add every permission used in code as a client role on that client, named `{module}.{action}`. The
   template needs `app-users.view-all`.
3. Group permissions into composite client roles (names without a dot) and assign those roles to users
   and service accounts.
4. Add an audience mapper that puts `KEYCLOAK_CLIENT_ID` into `aud` of tokens issued to the frontend and
   mobile clients that call this API. Turn off **Full scope allowed** on those clients and give them only
   this backend's client roles.
5. Keep this app's roles out of the realm default roles unless every new user should have them.

### Redis cache adapter lives under External

The template treats cache storage as external infrastructure. The contract is `IAppCache` in
`Contracts/External`, and the Redis implementation lives in `External/Cache`. The implementation
inherits `BaseCache`, so provider-neutral behavior such as serialization, key prefixing, cache results,
and cache-aside `GetOrSetAsync` comes from `alaasmagi.Base.Cache`.

The default cache entry uses an absolute expiration relative to now. Configure Redis and default cache
behavior with:

```text
REDIS_CONNECTION_STRING
CACHE_KEY_PREFIX
CACHE_DEFAULT_ABSOLUTE_EXPIRATION_SECONDS
```

### Web and API live in the same host

The template supports both MVC and JSON API endpoints from one ASP.NET Core host. This matches the
existing Alaasmagi backends and keeps authentication, configuration, and dependency injection in one
composition root.

### API versioning

APIs use URL-segment versioning through `Asp.Versioning.Mvc` and `Asp.Versioning.Mvc.ApiExplorer`.
The first version is exposed as `/api/v1/...`. Version reporting is enabled, so responses include the
supported API version headers.

### API rate limiting

API controllers use ASP.NET Core's built-in rate limiting middleware with a named fixed-window policy.
Configure it with:

```text
RATE_LIMIT_PERMIT_LIMIT
RATE_LIMIT_WINDOW_SECONDS
RATE_LIMIT_QUEUE_LIMIT
```

## Local Development

Restore and build:

```bash
dotnet restore alaasmagi-dotnet-template.sln
dotnet build alaasmagi-dotnet-template.sln
```

Run the tests:

```bash
dotnet test alaasmagi-dotnet-template.sln
```

Run the web host:

```bash
dotnet run --project Web/Web.csproj
```

Create a migration after changing persistence entities:

```bash
dotnet ef migrations add DbMigration-v1 --project DataAccess/DataAccess.csproj --startup-project Web/Web.csproj --context AppDbContext
```

Apply migrations:

```bash
dotnet ef database update --project DataAccess/DataAccess.csproj --startup-project Web/Web.csproj --context AppDbContext
```

## Adding a New Feature

Use `AppUser` as the reference implementation. For a new feature, add matching files across the same
layers and wire them in `Web/Configuration/ServiceConfiguration.cs`.

Prefer Base implementations first:

- Use `BaseEntity...` types for domain and persistence models.
- Use `BaseRepository` for EF repositories.
- Use `BaseService` for CRUD application services.
- Use `IAppCache` / `IBaseCache` for cache reads and writes.
- Use `BaseEventEnvelope<TContent>` for published events.
- Use `RabbitMqDeliveryConsumerBase<TContent>` for consumers that must choose between ack, requeue and
  reject, `RabbitMqConsumerBase<TContent>` for ack-or-reject consumers.
- Make message handling idempotent with the envelope `id` and `processed_messages`.
- Add unit tests for new logic in `Tests`.

Only write custom behavior where the feature differs from generic CRUD or generic messaging.
