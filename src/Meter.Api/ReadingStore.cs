using Microsoft.Data.Sqlite;

namespace Meter.Api;

public sealed record ReadingRequest(string OperationId, string MeterId, long ValueWh);
public sealed record Reading(string OperationId, string MeterId, long ValueWh);
public sealed record Submission(Reading Reading, bool Replay);
public sealed record IntegrationEvent(long EventId, string OperationId, string MeterId, long ValueWh);

public sealed class Rejected(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public interface IReadingStore
{
    Submission Submit(ReadingRequest request);
    Reading? Latest(string meterId);
    IReadOnlyList<IntegrationEvent> Events();
}

public sealed class ReadingStore : IReadingStore
{
    private readonly string connectionString;
    private readonly Action beforeCommit;

    public ReadingStore(string file, Action? beforeCommit = null)
    {
        this.beforeCommit = beforeCommit ?? (() => { });
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = file, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 5
        }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS meters (
                meter_id TEXT PRIMARY KEY,
                last_value_wh INTEGER NOT NULL CHECK(last_value_wh >= 0));
            CREATE TABLE IF NOT EXISTS readings (
                operation_id TEXT PRIMARY KEY,
                meter_id TEXT NOT NULL REFERENCES meters(meter_id),
                value_wh INTEGER NOT NULL CHECK(value_wh >= 0));
            CREATE TABLE IF NOT EXISTS outbox (
                event_id INTEGER PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE REFERENCES readings(operation_id),
                meter_id TEXT NOT NULL,
                value_wh INTEGER NOT NULL);
            INSERT OR IGNORE INTO meters VALUES ('MTR-001', 0), ('MTR-002', 0);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    public static string ValidateMeter(string meterId)
    {
        if (meterId is null || !System.Text.RegularExpressions.Regex.IsMatch(meterId, "^MTR-[0-9]{3}$"))
            throw new Rejected(400, "Use a meter identifier such as MTR-001.");
        return meterId;
    }

    public Submission Submit(ReadingRequest request)
    {
        if (!Guid.TryParseExact(request.OperationId, "D", out var operation))
            throw new Rejected(400, "Use a canonical GUID operation identifier.");
        ValidateMeter(request.MeterId);
        if (request.ValueWh is < 0 or > 1_000_000_000_000)
            throw new Rejected(400, "Reading must be 0..1000000000000 Wh.");
        var reading = new Reading(operation.ToString("D"), request.MeterId, request.ValueWh);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = "SELECT meter_id, value_wh FROM readings WHERE operation_id=$id";
        command.Parameters.AddWithValue("$id", reading.OperationId);
        using (var rows = command.ExecuteReader())
        {
            if (rows.Read())
            {
                if (rows.GetString(0) != reading.MeterId || rows.GetInt64(1) != reading.ValueWh)
                    throw new Rejected(409, "Operation identifier was reused with different content.");
                transaction.Commit();
                return new Submission(reading, true);
            }
        }

        command.Parameters.Clear();
        command.CommandText = "SELECT last_value_wh FROM meters WHERE meter_id=$meter";
        command.Parameters.AddWithValue("$meter", reading.MeterId);
        var previous = command.ExecuteScalar();
        if (previous is null)
            throw new Rejected(404, "Meter was not found.");
        if (reading.ValueWh < Convert.ToInt64(previous))
            throw new Rejected(409, "Cumulative meter readings must not decrease.");

        command.Parameters.AddWithValue("$id", reading.OperationId);
        command.Parameters.AddWithValue("$value", reading.ValueWh);
        command.CommandText = """
            INSERT INTO readings(operation_id,meter_id,value_wh) VALUES($id,$meter,$value);
            UPDATE meters SET last_value_wh=$value WHERE meter_id=$meter;
            INSERT INTO outbox(operation_id,meter_id,value_wh) VALUES($id,$meter,$value);
            """;
        command.ExecuteNonQuery();
        beforeCommit();
        transaction.Commit();
        return new Submission(reading, false);
    }

    public Reading? Latest(string meterId)
    {
        ValidateMeter(meterId);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT operation_id,meter_id,value_wh FROM readings
            WHERE meter_id=$meter ORDER BY rowid DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$meter", meterId);
        using var rows = command.ExecuteReader();
        return rows.Read() ? new Reading(rows.GetString(0), rows.GetString(1), rows.GetInt64(2)) : null;
    }

    public IReadOnlyList<IntegrationEvent> Events()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT event_id,operation_id,meter_id,value_wh FROM outbox ORDER BY event_id";
        using var rows = command.ExecuteReader();
        var events = new List<IntegrationEvent>();
        while (rows.Read())
            events.Add(new IntegrationEvent(rows.GetInt64(0), rows.GetString(1), rows.GetString(2), rows.GetInt64(3)));
        return events;
    }
}
