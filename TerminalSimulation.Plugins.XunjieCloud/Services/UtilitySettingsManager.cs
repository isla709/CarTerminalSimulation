using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TerminalSimulation.Plugins.XunjieCloud.Services
{
    public class SavedAccount
    {
        public string Username { get; set; } = string.Empty;
        public string EncryptedPassword { get; set; } = string.Empty;
        public int CredentialVersion { get; set; }
        public string LastDeviceNo { get; set; } = string.Empty;
    }

    public class UtilitySettings
    {
        public List<SavedAccount> SavedAccounts { get; set; } = new();
    }

    public static class UtilitySettingsManager
    {
        private static readonly string SettingsFile = Path.Combine(AppContext.BaseDirectory, "utility_settings.json");
        private const int CurrentCredentialVersion = 2;
        private static readonly byte[] LegacyKey = Encoding.UTF8.GetBytes("TerminalSimUtili808AESKey123456!");
        private static readonly byte[] LegacyIv = Encoding.UTF8.GetBytes("TerminalSimIV123");
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CarTerminalSimulation.XunjieCloud.v2");

        public static UtilitySettings LoadSettings()
        {
            if (!File.Exists(SettingsFile))
                return new UtilitySettings();

            try
            {
                string json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<UtilitySettings>(json) ?? new UtilitySettings();
            }
            catch
            {
                return new UtilitySettings();
            }
        }

        public static void SaveSettings(UtilitySettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            byte[] protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public static string Decrypt(string cipherText, int credentialVersion = CurrentCredentialVersion)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            try
            {
                return credentialVersion >= CurrentCredentialVersion
                    ? Encoding.UTF8.GetString(ProtectedData.Unprotect(
                        Convert.FromBase64String(cipherText), Entropy, DataProtectionScope.CurrentUser))
                    : DecryptLegacy(cipherText);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string DecryptLegacy(string cipherText)
        {
            byte[] buffer = Convert.FromBase64String(cipherText);
            using Aes aes = Aes.Create();
            aes.Key = LegacyKey;
            aes.IV = LegacyIv;
            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(buffer);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            return sr.ReadToEnd();
        }

        public static void SaveAccount(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username)) return;
            
            var settings = LoadSettings();
            var existing = settings.SavedAccounts.Find(a => a.Username == username);
            if (existing != null)
            {
                existing.EncryptedPassword = Encrypt(password);
                existing.CredentialVersion = CurrentCredentialVersion;
            }
            else
            {
                settings.SavedAccounts.Add(new SavedAccount { Username = username, EncryptedPassword = Encrypt(password), CredentialVersion = CurrentCredentialVersion });
            }
            SaveSettings(settings);
        }

        public static void UpdateLastDeviceNo(string username, string deviceNo)
        {
            if (string.IsNullOrWhiteSpace(username)) return;
            var settings = LoadSettings();
            var existing = settings.SavedAccounts.Find(a => a.Username == username);
            if (existing != null)
            {
                existing.LastDeviceNo = deviceNo;
                SaveSettings(settings);
            }
        }
    }
}
