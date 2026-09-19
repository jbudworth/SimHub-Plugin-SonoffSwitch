using System;
using System.Security.Cryptography;
using System.Text;

namespace SimHub.Plugin.SonoffSwitch
{
    /// <summary>
    /// Encrypts device passwords at rest using Windows DPAPI (CurrentUser scope), so the
    /// plugin's saved settings file holds ciphertext rather than plaintext. DPAPI ties the
    /// ciphertext to the Windows account that encrypted it: the same user, on the same
    /// machine, can decrypt it transparently (no key file to manage), but it will not
    /// decrypt if the settings file is copied to a different machine or user account - in
    /// that case the device's password will simply need to be re-entered.
    /// </summary>
    public static class PasswordProtector
    {
        // Additional entropy scopes the encryption to this plugin, so its ciphertext can't
        // be unprotected by some unrelated application running under the same Windows account.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SimHub.Plugin.SonoffSwitch.DeviceAuth.v1");

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            var bytes = Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public static bool TryDecrypt(string storedValue, out string plainText)
        {
            plainText = string.Empty;
            if (string.IsNullOrEmpty(storedValue))
                return true;

            try
            {
                var bytes = Convert.FromBase64String(storedValue);
                var unprotectedBytes = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
                plainText = Encoding.UTF8.GetString(unprotectedBytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Decrypts a stored value, falling back to returning it unchanged if it isn't
        /// valid ciphertext (e.g. a password saved by a version of this plugin from before
        /// encryption support was added).
        /// </summary>
        public static string Decrypt(string storedValue)
        {
            return TryDecrypt(storedValue, out var plainText) ? plainText : storedValue;
        }

        /// <summary>
        /// Ensures a stored value is encrypted, migrating plaintext left over from a
        /// pre-encryption version of this plugin the first time it's touched.
        /// </summary>
        public static string EnsureEncrypted(string storedValue)
        {
            if (string.IsNullOrEmpty(storedValue))
                return storedValue;

            return TryDecrypt(storedValue, out _) ? storedValue : Encrypt(storedValue);
        }
    }
}
