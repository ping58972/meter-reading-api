# Verification plan and threat model

## Requirement coverage

| Requirement | Test evidence |
| --- | --- |
| Accept a valid reading and return its saved values | SubmitAndReplayCreateOneEvent; HttpCreateReplayReadAndEventContract |
| Identical retries produce one reading and event | SubmitAndReplayCreateOneEvent; ConcurrentRetriesConverge |
| Changed replays and decreasing readings conflict | ChangedReplayAndDecreasingValuesConflict |
| Enforce inclusive numeric boundaries | RejectInvalidValues; AcceptInclusiveValueBoundaries |
| Validate operation and meter identifiers | RejectUnknownMeterAndInvalidIdentifiers |
| Roll back reading, meter update, and outbox together | RollbackIncludesReadingMeterAndOutbox |
| Persist values after reopening | ReopenPreservesResults |
| Require authentication and role scopes | RejectUnauthenticatedWrites; EnforceRoles |
| Reject malformed, missing, duplicate, and unknown fields | RejectMalformedInputWithoutWrites; StrictJsonRejectsAmbiguousRequests |
| Prevent shared writer/reader credentials | RejectsAmbiguousCredentials |
| Recover from lost client acknowledgements | Companion client tools/integration.ts |

## Data flow and trust boundaries

A field client sends an authenticated HTTP request to the API. Authorization separates the writer from the event reader. The storage adapter validates the domain contract and writes a relational transaction. SQLite commits the reading, cumulative value, and event together.

The native client's key storage and the server's environment configuration form separate secret boundaries. Synthetic reading data crosses the HTTP boundary. The demo binds local development requests to loopback, and the native client permits HTTPS remote origins.

## Threat scenarios

| Scenario | Implemented response | Further deployment work |
| --- | --- | --- |
| Stolen or overprivileged credential | Distinct role-scoped keys and authorization tests. | Managed identity, expiry, rotation, and per-device enrollment. |
| Replay or duplicate retry | Stable operation ID, exact payload comparison, unique reading key. | Fleet-wide tenant separation and retention policy. |
| SQL injection | Parameterized statements and identifier validation. | Database deployment controls and operational monitoring. |
| Ambiguous input | Required constructor fields, duplicate-field rejection, unknown-field rejection. | Explicit versioning and compatibility tests. |
| Broken transaction | Fault-injected exception before commit, followed by checks of all affected tables. | Storage-device fault tests and backup/restore rehearsals. |
| Interception | Native client requires HTTPS for remote origins. | TLS deployment, certificate lifecycle, and device trust management. |
| Oversized payload | Kestrel body limit of 4 KiB. | Rate limiting and edge limits. |

These are design and verification records for the supplied prototype. Organization-specific SDLC review and formal compliance assessment require the target deployment and review process.

## Manual acceptance sequence

1. Configure two distinct keys and start the API.
2. Submit the sample reading in the README and expect 201.
3. Repeat the same operation and expect 200 with replay=true.
4. Inspect events with the reader credential and expect one event.
5. Submit a lower value with a new operation and expect 409.
6. Attempt to submit with the reader key and expect 403.
7. Restart using the same database path and read the last value.
8. Run the companion client's offline recovery script against the built DLL.

Record inputs, expected/actual response, implementation commit, and failure details for each run.
