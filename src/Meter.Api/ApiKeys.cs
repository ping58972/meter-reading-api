using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Meter.Api;

public sealed class ApiKeys
{
    public byte[] Writer { get; }
    public byte[] Reader { get; }

    public ApiKeys(string? writer, string? reader)
    {
        if (writer is null || reader is null || writer.Length < 32 || reader.Length < 32 || writer == reader)
            throw new InvalidOperationException("Configure distinct MeterWriterKey and MeterReaderKey values of at least 32 characters.");
        Writer = Encoding.UTF8.GetBytes("Bearer " + writer);
        Reader = Encoding.UTF8.GetBytes("Bearer " + reader);
    }
}

public sealed class KeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, ApiKeys keys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.Authorization.Count != 1)
            return Task.FromResult(AuthenticateResult.NoResult());
        var supplied = Encoding.UTF8.GetBytes(Request.Headers.Authorization.ToString());
        string? role = CryptographicOperations.FixedTimeEquals(supplied, keys.Writer) ? "writer" :
            CryptographicOperations.FixedTimeEquals(supplied, keys.Reader) ? "reader" : null;
        if (role is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid bearer credential."));
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
