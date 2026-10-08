using Handshake.Desktop.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace Handshake.Desktop.Services;

public sealed class LocalStore
{
    private readonly string _connectionString;

    public LocalStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Handshake");
        Directory.CreateDirectory(folder);
        _connectionString = $"Data Source={Path.Combine(folder, "handshake.db")}";
        Initialize();
    }

    public EventRecord GetOrCreateDefaultEvent()
    {
        using var connection = Open();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT id, name, location, dates FROM events ORDER BY created_at LIMIT 1";
        using var result = read.ExecuteReader();
        if (result.Read()) return new EventRecord(result.GetString(0), result.GetString(1), result.GetString(2), result.GetString(3));

        var created = new EventRecord(Guid.NewGuid().ToString(), "New event", "", "");
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO events(id, name, location, dates, created_at) VALUES ($id, $name, $location, $dates, $created)";
        insert.Parameters.AddWithValue("$id", created.Id);
        insert.Parameters.AddWithValue("$name", created.Name);
        insert.Parameters.AddWithValue("$location", created.Location);
        insert.Parameters.AddWithValue("$dates", created.Dates);
        insert.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        insert.ExecuteNonQuery();
        return created;
    }

    public void SaveCapture(EventRecord eventRecord, IEnumerable<CaptureImage> images)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO captures(id, event_id, images, state, created_at) VALUES ($id, $event, $images, 'draft', $created)";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$event", eventRecord.Id);
        command.Parameters.AddWithValue("$images", System.Text.Json.JsonSerializer.Serialize(images.Select(x => x.Path)));
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS events (id TEXT PRIMARY KEY, name TEXT NOT NULL, location TEXT NOT NULL, dates TEXT NOT NULL, created_at INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS captures (id TEXT PRIMARY KEY, event_id TEXT NOT NULL, images TEXT NOT NULL, state TEXT NOT NULL, created_at INTEGER NOT NULL, FOREIGN KEY(event_id) REFERENCES events(id));";
        command.ExecuteNonQuery();
    }
}
