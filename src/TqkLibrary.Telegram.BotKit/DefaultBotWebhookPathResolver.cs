using System.Security.Cryptography;
using System.Text;

namespace TqkLibrary.Telegram.BotKit
{
    /// <summary>
    /// Default <see cref="IBotWebhookPathResolver"/>: lowercase hex of SHA-256(botToken).
    /// 64-char output, stable across processes, safe inside Telegram secret_token charset.
    /// </summary>
    public sealed class DefaultBotWebhookPathResolver : IBotWebhookPathResolver
    {
        public string ResolvePath(string botToken)
        {
            if (botToken is null) throw new ArgumentNullException(nameof(botToken));

            byte[] hash;
            using (SHA256 sha = SHA256.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(botToken));

            char[] chars = new char[hash.Length * 2];
            for (int i = 0; i < hash.Length; i++)
            {
                int b = hash[i];
                chars[i * 2] = ToHexLower(b >> 4);
                chars[i * 2 + 1] = ToHexLower(b & 0xF);
            }
            return new string(chars);
        }

        static char ToHexLower(int v) => (char)(v < 10 ? '0' + v : 'a' + (v - 10));
    }
}
