namespace ToastRevival.Api.DTOs;

public record BlocklistEntryResponse(Guid Id, string Term, DateTime CreatedAt);
public record AddBlocklistEntryRequest(string Term);
