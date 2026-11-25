# Meter Reading API

A C#/.NET 10 microservice prototype for collecting cumulative readings from synthetic utility meters. It provides an ASP.NET Core HTTP API, relational SQLite persistence, scoped bearer credentials, replay-safe submissions, and transactional integration-event storage.

A field device can lose its connection after a server accepts a reading. This API preserves an operation ID so the device can retry and receive the same result while storing one reading and one integration event.

## Honeywell role alignment

Honeywell requisition 152359 is a Software Engineer II role for Elster Solutions in Raleigh. The posting requests .NET/C#, SQL, APIs/microservices, mobile frameworks, tests, CI/CD, and security design. This project addresses the service, database, integration, testing, and pipeline requirements. [Official posting](https://ibqbjb.fa.ocs.oraclecloud.com/hcmUI/CandidateExperience/en/sites/Honeywell/job/152359)

Honeywell describes connected automation across building, industrial, and process operations. The synthetic metering scenario provides an application context for a field-reading workflow. [Company website](https://www.honeywell.com/us/en)

## Build and run

Use the .NET 10 SDK.

~~~sh
git clone https://github.com/ping58972/meter-reading-api.git
cd meter-reading-api
dotnet test tests/Meter.Tests -c Release
export MeterWriterKey="$(python3 -c 'import secrets; print(secrets.token_urlsafe(32))')"
export MeterReaderKey="$(python3 -c 'import secrets; print(secrets.token_urlsafe(32))')"
dotnet run --project src/Meter.Api --urls http://127.0.0.1:5079
~~~

Use the same environment variables in the terminal making requests.

~~~sh
curl --fail-with-body http://127.0.0.1:5079/readings \
  -H "Authorization: Bearer $MeterWriterKey" -H 'Content-Type: application/json' \
  --data '{"operationId":"00000000-0000-4000-8000-000000000001","meterId":"MTR-001","valueWh":100}'

curl --fail-with-body http://127.0.0.1:5079/meters/MTR-001/latest \
  -H "Authorization: Bearer $MeterReaderKey"

curl --fail-with-body http://127.0.0.1:5079/events \
  -H "Authorization: Bearer $MeterReaderKey"
~~~

The first submission returns HTTP 201 and an identical retry returns 200 with `replay=true`. Reusing an operation ID for different content returns 409. A lower cumulative reading also returns 409. The seeded meters are MTR-001 and MTR-002.

## Architecture and skills

| Component | Implementation |
| --- | --- |
| HTTP adapter | ASP.NET Core minimal endpoints, typed contracts, strict JSON parsing, authorization policies, and bounded Kestrel request bodies. |
| Storage boundary | IReadingStore interface with a SQLite implementation and parameterized SQL. |
| Submission transaction | Immediate transaction verifies replay content, inserts a reading, updates the meter, and adds an outbox event atomically. |
| Error handling | Explicit validation/conflict codes and generic storage-availability errors. |
| Authentication | Distinct writer/reader keys with scoped roles and fixed-time key comparisons. |
| Testing | xUnit boundary, rollback, concurrency, persistence, authorization, and in-process HTTP tests. |
| Delivery | Multi-stage Dockerfile, GitHub Actions pipeline, and Jenkins declarative pipeline. |

Schema relationships connect meters to readings and readings to outbox events through foreign keys. Operation IDs are unique. The database uses WAL mode and bounded lock waits. The outbox exposes stored integration events for inspection. A downstream dispatch worker is a future extension.

The [companion mobile client](https://github.com/ping58972/field-sync-client) retains pending readings locally and retries the same operation ID after an ambiguous acknowledgement. Its integration script exercises the real API and SQLite database across a process restart.

## Container

~~~sh
docker build -t meter-reading-api .
docker run --rm -p 127.0.0.1:5079:8080 \
  -e MeterWriterKey -e MeterReaderKey meter-reading-api
~~~

Use a mounted writable directory and MeterDbPath when you want database persistence beyond container removal.

## Verification and security design

See [test procedures and threat model](docs/verification.md). Local validation on 2026-11-25 passed 20 xUnit cases on macOS ARM64. The mobile integration test also passed offline recovery, same-operation retry, conflict retention, and database persistence.

The NuGet dependency check reported zero known vulnerable packages in the configured sources at validation time.

~~~sh
dotnet list tests/Meter.Tests package --vulnerable --include-transitive
~~~

The GitHub Actions workflow runs tests and packages the API. The Jenkinsfile contains equivalent build/test/package stages and a dependency inventory command. Jenkins execution requires an agent with the .NET 10 SDK.

## Scope and next steps

This portfolio prototype uses synthetic meters and local credentials. Production extensions include a managed identity provider, TLS termination, key rotation, event dispatch, schema migrations, and deployment-specific telemetry. The threat model documents these boundaries and the implemented mitigations.

### Resume bullet

Built a C#/.NET metering API with SQLite transactions, idempotent submissions, scoped access, integration-event storage, and automated unit/integration tests.

Created by [Ping](https://github.com/ping58972).
