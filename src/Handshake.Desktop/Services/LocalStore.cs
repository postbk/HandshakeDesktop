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

    public EventRecord CreateEvent(string name, string location, string dates)
    {
        var created = new EventRecord(Guid.NewGuid().ToString(), name.Trim(), location.Trim(), dates.Trim());
        using var connection = Open(); using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO events(id, name, location, dates, created_at) VALUES ($id, $name, $location, $dates, $created)";
        insert.Parameters.AddWithValue("$id", created.Id); insert.Parameters.AddWithValue("$name", created.Name); insert.Parameters.AddWithValue("$location", created.Location); insert.Parameters.AddWithValue("$dates", created.Dates); insert.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); insert.ExecuteNonQuery(); return created;
    }

    public List<EventRecord> GetEvents()
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT id, name, location, dates FROM events ORDER BY created_at DESC"; using var rows = command.ExecuteReader(); var events = new List<EventRecord>(); while (rows.Read()) events.Add(new EventRecord(rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3))); return events;
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

    public void SaveContact(ContactRecord contact)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO contacts(id, event_id, name, company, title, email, phone, website, address, raw_extraction, notes, follow_up, done, created_at) VALUES ($id, $event, $name, $company, $title, $email, $phone, $website, $address, $raw, $notes, $follow, $done, $created)";
        command.Parameters.AddWithValue("$id", contact.Id); command.Parameters.AddWithValue("$event", contact.EventId);
        command.Parameters.AddWithValue("$name", contact.Name); command.Parameters.AddWithValue("$company", contact.Company);
        command.Parameters.AddWithValue("$title", contact.Title); command.Parameters.AddWithValue("$email", contact.Email);
        command.Parameters.AddWithValue("$phone", contact.Phone); command.Parameters.AddWithValue("$website", contact.Website);
        command.Parameters.AddWithValue("$address", contact.Address); command.Parameters.AddWithValue("$raw", contact.RawExtraction);
        command.Parameters.AddWithValue("$notes", contact.Notes); command.Parameters.AddWithValue("$follow", contact.FollowUp); command.Parameters.AddWithValue("$done", contact.Done ? 1 : 0);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); command.ExecuteNonQuery();
    }

    public List<ContactRecord> GetContacts(string eventId)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, event_id, name, company, title, email, phone, website, address, raw_extraction FROM contacts WHERE event_id = $event ORDER BY created_at DESC";
        command.Parameters.AddWithValue("$event", eventId);
        using var rows = command.ExecuteReader(); var contacts = new List<ContactRecord>();
        while (rows.Read()) contacts.Add(new ContactRecord(rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3), rows.GetString(4), rows.GetString(5), rows.GetString(6), rows.GetString(7), rows.GetString(8), rows.GetString(9), rows.IsDBNull(10) ? "" : rows.GetString(10), rows.IsDBNull(11) ? "" : rows.GetString(11), !rows.IsDBNull(12) && rows.GetInt64(12) != 0));
        return contacts;
    }

    public void ExportCsv(string eventId, string path)
    {
        var rows = GetContacts(eventId);
        using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
        writer.WriteLine("name,company,title,email,phone,website,address,notes,follow_up,follow_up_done");
        foreach (var c in rows) writer.WriteLine(string.Join(",", new[] { c.Name, c.Company, c.Title, c.Email, c.Phone, c.Website, c.Address, c.Notes, c.FollowUp, c.Done ? "true" : "false" }.Select(CsvCell)));
    }

    public void ExportVcf(string eventId, string path)
    {
        using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false));
        foreach (var c in GetContacts(eventId))
        {
            writer.WriteLine("BEGIN:VCARD\nVERSION:3.0");
            writer.WriteLine($"FN:{VCard(c.Name)}");
            if (!string.IsNullOrWhiteSpace(c.Company)) writer.WriteLine($"ORG:{VCard(c.Company)}");
            if (!string.IsNullOrWhiteSpace(c.Title)) writer.WriteLine($"TITLE:{VCard(c.Title)}");
            if (!string.IsNullOrWhiteSpace(c.Email)) writer.WriteLine($"EMAIL:{VCard(c.Email)}");
            if (!string.IsNullOrWhiteSpace(c.Phone)) writer.WriteLine($"TEL:{VCard(c.Phone)}");
            if (!string.IsNullOrWhiteSpace(c.Website)) writer.WriteLine($"URL:{VCard(c.Website)}");
            if (!string.IsNullOrWhiteSpace(c.Address)) writer.WriteLine($"ADR:;;{VCard(c.Address)}");
            if (!string.IsNullOrWhiteSpace(c.Notes) || !string.IsNullOrWhiteSpace(c.FollowUp)) writer.WriteLine($"NOTE:{VCard(string.Join("\n", new[] { c.Notes, string.IsNullOrWhiteSpace(c.FollowUp) ? "" : $"Follow-up ({(c.Done ? "done" : "open")}): {c.FollowUp}" }.Where(x => !string.IsNullOrWhiteSpace(x))))}");
            writer.WriteLine("END:VCARD");
        }
    }

    private static string VCard(string value) => value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");

    private static string CsvCell(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

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
        command.CommandText = "CREATE TABLE IF NOT EXISTS events (id TEXT PRIMARY KEY, name TEXT NOT NULL, location TEXT NOT NULL, dates TEXT NOT NULL, created_at INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS captures (id TEXT PRIMARY KEY, event_id TEXT NOT NULL, images TEXT NOT NULL, state TEXT NOT NULL, created_at INTEGER NOT NULL, FOREIGN KEY(event_id) REFERENCES events(id)); CREATE TABLE IF NOT EXISTS contacts (id TEXT PRIMARY KEY, event_id TEXT NOT NULL, name TEXT NOT NULL, company TEXT NOT NULL, title TEXT NOT NULL, email TEXT NOT NULL, phone TEXT NOT NULL, website TEXT NOT NULL, address TEXT NOT NULL, raw_extraction TEXT NOT NULL, notes TEXT NOT NULL DEFAULT '', follow_up TEXT NOT NULL DEFAULT '', done INTEGER NOT NULL DEFAULT 0, created_at INTEGER NOT NULL, FOREIGN KEY(event_id) REFERENCES events(id));";
        command.ExecuteNonQuery();
        foreach (var sql in new[] { "ALTER TABLE contacts ADD COLUMN notes TEXT NOT NULL DEFAULT ''", "ALTER TABLE contacts ADD COLUMN follow_up TEXT NOT NULL DEFAULT ''", "ALTER TABLE contacts ADD COLUMN done INTEGER NOT NULL DEFAULT 0" })
        { try { using var migration = connection.CreateCommand(); migration.CommandText = sql; migration.ExecuteNonQuery(); } catch (Microsoft.Data.Sqlite.SqliteException) { } }
    }
}
