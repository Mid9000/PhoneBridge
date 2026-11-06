using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PhoneBridge.Core;

namespace PhoneBridge.Server;

/// <summary>Self-signed TLS certificate, generated once and persisted (PFX protected by a password kept in the secret store).</summary>
public static class CertificateManager
{
    const string PasswordKey = "PhoneBridge/cert-password";

    public static X509Certificate2 LoadOrCreate(string pfxPath, ISecretStore secrets)
    {
        var password = secrets.Get(PasswordKey);
        if (password is not null && File.Exists(pfxPath))
        {
            try { return new X509Certificate2(pfxPath, password, X509KeyStorageFlags.UserKeySet); }
            catch (CryptographicException) { /* wrong password or corrupt file: regenerate (paired phones must re-pair) */ }
        }

        password = Tokens.NewToken();
        var pfx = CreatePfx(password);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pfxPath))!);
        File.WriteAllBytes(pfxPath, pfx);
        secrets.Set(PasswordKey, password);
        return new X509Certificate2(pfx, password, X509KeyStorageFlags.UserKeySet);
    }

    /// <summary>Lowercase hex SHA-256 of the DER certificate, as pinned by the iPhone (docs/protocol.md).</summary>
    public static string Sha256(X509Certificate2 cert) => Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();

    static byte[] CreatePfx(string password)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=PhoneBridge", key, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName(Environment.MachineName.ToLowerInvariant() + ".local");
        san.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(san.Build());
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return cert.Export(X509ContentType.Pfx, password);
    }
}
