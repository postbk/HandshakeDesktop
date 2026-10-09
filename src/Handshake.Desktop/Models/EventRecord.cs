namespace Handshake.Desktop.Models;

public sealed record EventRecord(string Id, string Name, string Location, string Dates);
public sealed record ContactRecord(string Id, string EventId, string Name, string Company, string Title, string Email, string Phone, string Website, string Address, string RawExtraction, string Notes = "", string FollowUp = "", bool Done = false, string Research = "", string PhotoPath = "", string CardImagePath = "", string LinkedInUrl = "");
