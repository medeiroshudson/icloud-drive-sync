namespace ICloudDriveSync.Auth;

/// <summary>URLs dos webservices do iCloud obtidas no accountLogin.</summary>
public sealed record WebServices(string DriveWsUrl, string DocWsUrl);

public abstract record AuthResult;

public sealed record AuthSuccess(WebServices Services) : AuthResult;

/// <summary>Sessão inválida/expirada — o serviço deve alertar, nunca re-autenticar com senha (SRP).</summary>
public sealed record AuthRequired(string Reason) : AuthResult;
