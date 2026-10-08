using Handshake.Desktop.Models;
using Handshake.Desktop.Services;
using Xunit;

namespace Handshake.Desktop.Tests;

public sealed class ExportTests
{
    [Fact]
    public void ContactRecord_PreservesActionFields()
    {
        var record = new ContactRecord("id", "event", "Ada", "Example Co", "CEO", "a@example.com", "555", "https://example.com", "1 Main St", "{}", "Met at booth", "Send pricing", false);
        Assert.Equal("Send pricing", record.FollowUp);
        Assert.False(record.Done);
    }
}
