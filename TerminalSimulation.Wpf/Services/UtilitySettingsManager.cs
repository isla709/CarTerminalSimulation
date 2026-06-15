using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TerminalSimulation.Wpf.Services
{
    public class SavedAccount
    {
        public string Username { get; set; } = string.Empty;
        public string EncryptedPassword { get; set; } = string.Empty;
        public string LastDeviceNo { get; set; } = string.Empty;
    }

    public class UtilitySettings
    {
        public List<SavedAccount> SavedAccounts { get; set; } = new();
    }

    public static class UtilitySettingsManager
    {
        private static readonly string SettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "utility_settings.json");
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("TerminalSimUtili808AESKey123456!"); // 32 bytes
        private static readonly byte[] Iv = Encoding.UTF8.GetBytes("TerminalSimIV123"); // 16 bytes

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
            try
            {
                using Aes aes = Aes.Create();
                aes.Key = Key;
                aes.IV = Iv;
                using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream();
                using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
                using (var sw = new StreamWriter(cs))
                {
                    sw.Write(plainText);
                }
                return Convert.ToBase64String(ms.ToArray());
            }
            catch
            {
                return plainText; // Fallback
            }
        }

        public static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            try
            {
                byte[] buffer = Convert.FromBase64String(cipherText);
                using Aes aes = Aes.Create();
                aes.Key = Key;
                aes.IV = Iv;
                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream(buffer);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);
                return sr.ReadToEnd();
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void SaveAccount(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username)) return;
            
            var settings = LoadSettings();
            var existing = settings.SavedAccounts.Find(a => a.Username == username);
            if (existing != null)
            {
                existing.EncryptedPassword = Encrypt(password);
            }
            else
            {
                settings.SavedAccounts.Add(new SavedAccount { Username = username, EncryptedPassword = Encrypt(password) });
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
