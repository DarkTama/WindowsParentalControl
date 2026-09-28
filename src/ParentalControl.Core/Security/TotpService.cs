using System.Security.Cryptography;
using System.Text;

namespace ParentalControl.Core.Security;

public static class TotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret(int byteLength = 20)
    {
        var bytes = new byte[byteLength];
        RandomNumberGenerator.Fill(bytes);
        return ToBase32String(bytes);
    }

    public static string GenerateOtpauthUri(string issuer, string account, string secret)
    {
        var escapedIssuer = Uri.EscapeDataString(issuer);
        var escapedAccount = Uri.EscapeDataString(account);
        return $"otpauth://totp/{escapedIssuer}:{escapedAccount}?secret={secret}&issuer={escapedIssuer}&algorithm=SHA1&digits=6&period=30";
    }

    public static bool VerifyCode(string secret, string code, int allowedDrift = 1)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
            return false;

        code = code.Trim();
        if (code.Length != 6) return false;

        byte[] keyBytes;
        try
        {
            keyBytes = FromBase32String(secret);
        }
        catch
        {
            return false;
        }

        var currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        for (var drift = -allowedDrift; drift <= allowedDrift; drift++)
        {
            var expectedCode = ComputeTotp(keyBytes, currentStep + drift);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expectedCode),
                    Encoding.UTF8.GetBytes(code)))
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeTotp(byte[] key, long step)
    {
        var stepBytes = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(stepBytes);
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(stepBytes);

        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
                       | ((hash[offset + 1] & 0xFF) << 16)
                       | ((hash[offset + 2] & 0xFF) << 8)
                       | (hash[offset + 3] & 0xFF);

        var otp = binaryCode % 1000000;
        return otp.ToString("D6");
    }

    private static string ToBase32String(byte[] data)
    {
        var result = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                result.Append(Base32Alphabet[(buffer >> bitsLeft) & 31]);
            }
        }

        if (bitsLeft > 0)
        {
            result.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        }

        return result.ToString();
    }

    private static byte[] FromBase32String(string input)
    {
        input = input.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>();
        int buffer = 0, bitsLeft = 0;

        foreach (var c in input)
        {
            var val = Base32Alphabet.IndexOf(c);
            if (val < 0) continue;

            buffer = (buffer << 5) | val;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return output.ToArray();
    }
}
