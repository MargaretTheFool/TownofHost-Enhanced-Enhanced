using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOHE.Modules;

internal static class CloudBinding
{
    internal static bool TryCreateLink(Uri origin, string token, out string url)
        => TryCreateLink(origin, token, null, out url);

    internal static bool TryCreateLink(Uri origin, string token, IReadOnlyList<CloudAccount.BindingPublicKey> keys, out string url)
    {
        url = null;
        if (origin == null || string.IsNullOrWhiteSpace(token)) return false;
        byte[] key = null;
        try
        {
            using var rsa = RSA.Create();
            string kid = null;
            if (keys != null)
            {
                foreach (var publicKey in keys)
                {
                    if (publicKey.Kty != "RSA" || string.IsNullOrWhiteSpace(publicKey.Kid)) continue;
                    try
                    {
                        rsa.ImportParameters(new RSAParameters { Modulus = Decode(publicKey.N), Exponent = Decode(publicKey.E) });
                        if (rsa.KeySize < 2048) continue;
                        kid = publicKey.Kid;
                        break;
                    }
                    catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) { }
                }
                // The advertised set, including an empty set, is authoritative:
                // a bundled key may have been removed after a rotation.
                if (kid == null) return false;
            }
            else
            {
                var assembly = Assembly.GetExecutingAssembly();
                string resource = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(".CloudBinding.json", StringComparison.Ordinal));
                if (resource == null) return false;
                using var stream = assembly.GetManifestResourceStream(resource);
                using var document = JsonDocument.Parse(stream);
                kid = document.RootElement.GetProperty("kid").GetString();
                string pem = document.RootElement.GetProperty("public_key_pem").GetString();
                if (string.IsNullOrWhiteSpace(kid) || string.IsNullOrWhiteSpace(pem)
                    || !pem.TrimStart().StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal)) return false;
                rsa.ImportFromPem(pem);
                if (rsa.KeySize < 2048) return false;
            }

            key = RandomNumberGenerator.GetBytes(32);
            byte[] iv = RandomNumberGenerator.GetBytes(12);
            string header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RSA-OAEP-256", enc = "A256GCM", kid, typ = "JWE" }));
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(new
            {
                audience = "tohee-cloud:account-binding", token,
                nonce = Encode(RandomNumberGenerator.GetBytes(16))
            });
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];
            using (var aes = new AesGcm(key)) aes.Encrypt(iv, plaintext, ciphertext, tag, Encoding.ASCII.GetBytes(header));
            string payload = string.Join('.', header, Encode(rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256)),
                Encode(iv), Encode(ciphertext), Encode(tag));
            url = new Uri(origin, "/bind/mod?payload=" + Uri.EscapeDataString(payload)).AbsoluteUri;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
        finally
        {
            if (key != null) CryptographicOperations.ZeroMemory(key);
        }
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
    }
}
