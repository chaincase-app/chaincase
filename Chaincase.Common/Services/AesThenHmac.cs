/*
 * based on Modern Encryption of a String C#, by James Tuley, 
 * https://gist.github.com/4336842
 * 
 * Dan Gould 2021
 */

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Chaincase.Common.Services
{
    public static class AesThenHmac
    {
        private static readonly RandomNumberGenerator Random = RandomNumberGenerator.Create();

        //Preconfigured Encryption Parameters
        public static readonly int BlockBitSize = 128;
        public static readonly int KeyBitSize = 256;
        public static readonly int FatKeyBitSize = KeyBitSize * 2;

        //Preconfigured Password Key Derivation Parameters
        public static readonly int SaltBitSize = 64;
        public static readonly int Iterations = 600_000;

        /// <summary>
        /// Helper that generates a random cryptKey concat authKey on each call.
        /// </summary>
        /// <returns></returns>
        public static byte[] NewKey()
        {
            // key the size of cryptKey + size of authKey
            var key = new byte[KeyBitSize * 2 / 8];
            Random.GetBytes(key);
            return key;
        }

        /// <summary>
        ///  Encryption (AES) then Authentication (HMAC) for a UTF8 Message.
        /// </summary>
        /// <param name="secretMessage">The secret message.</param>
        /// <param name="cryptKey">The crypt key.</param>
        /// <param name="authKey">The auth key.</param>
        /// <param name="nonSecretPayload">(Optional) Non-Secret Payload.</param>
        /// <returns>
        /// Encrypted Message
        /// </returns>
        /// <exception cref="System.ArgumentException">Secret Message Required!;secretMessage</exception>
        /// <remarks>
        /// Adds overhead of (Optional-Payload + BlockSize(16) + Message-Padded-To-Blocksize +  HMac-Tag(32)) * 1.33 Base64
        /// </remarks>
        public static string Encrypt(string secretMessage, byte[] cryptKey, byte[] authKey,
                           byte[] nonSecretPayload = null)
        {
            if (string.IsNullOrEmpty(secretMessage))
                throw new ArgumentException("Secret Message Required!", "secretMessage");

            var plainText = Encoding.UTF8.GetBytes(secretMessage);
            var cipherText = Encrypt(plainText, cryptKey, authKey, nonSecretPayload);
            return Convert.ToBase64String(cipherText);
        }

        /// <summary>
        ///  Encryption (AES) then Authentication (HMAC) for a UTF8 Message.
        /// </summary>
        /// <param name="secretMessage">The secret message.</param>
        /// <param name="fatKey">The crypt key concatenaded with the auth key.</param>
        /// <param name="nonSecretPayload">(Optional) Non-Secret Payload.</param>
        /// <returns>
        /// Encrypted Message
        /// </returns>
        /// <exception cref="System.ArgumentException">Secret Message Required!;secretMessage</exception>
        /// <remarks>
        /// Adds overhead of (Optional-Payload + BlockSize(16) + Message-Padded-To-Blocksize +  HMac-Tag(32)) * 1.33 Base64
        /// </remarks>
        public static string Encrypt(string secretMessage, byte[] fatKey,
                            byte[] nonSecretPayload = null)
        {
            // Error Checks
            if (fatKey == null || fatKey.Length != FatKeyBitSize / 8)
                throw new ArgumentException(String.Format("Key needs to be {0} bit!", FatKeyBitSize), "fatKey");

            byte[] cryptKey = fatKey.Take(fatKey.Length / 2).ToArray();
            byte[] authKey = fatKey.Skip(fatKey.Length / 2).ToArray();
            return Encrypt(secretMessage, cryptKey, authKey, nonSecretPayload);
        }

        /// <summary>
        ///  Authentication (HMAC) then Decryption (AES) for a secrets UTF8 Message.
        /// </summary>
        /// <param name="encryptedMessage">The encrypted message.</param>
        /// <param name="cryptKey">The crypt key.</param>
        /// <param name="authKey">The auth key.</param>
        /// <param name="nonSecretPayloadLength">Length of the non secret payload.</param>
        /// <returns>
        /// Decrypted Message
        /// </returns>
        /// <exception cref="System.ArgumentException">Encrypted Message Required!;encryptedMessage</exception>
        public static string Decrypt(string encryptedMessage, byte[] cryptKey, byte[] authKey,
                           int nonSecretPayloadLength = 0)
        {
            if (string.IsNullOrWhiteSpace(encryptedMessage))
                throw new ArgumentException("Encrypted Message Required!", "encryptedMessage");

            var cipherText = Convert.FromBase64String(encryptedMessage);
            var plainText = Decrypt(cipherText, cryptKey, authKey, nonSecretPayloadLength);
            return plainText == null ? null : Encoding.UTF8.GetString(plainText);
        }

        /// <summary>
        ///  Authentication (HMAC) then Decryption (AES) for a secrets UTF8 Message.
        /// </summary>
        /// <param name="encryptedMessage">The encrypted message.</param>
        /// <param name="fatKey">The crypt key concatenated with the auth key.</param>
        /// <param name="nonSecretPayloadLength">Length of the non secret payload.</param>
        /// <returns>
        /// Decrypted Message
        /// </returns>
        /// <exception cref="System.ArgumentException">Encrypted Message Required!;encryptedMessage</exception>
        public static string Decrypt(string secretMessage, byte[] fatKey,
                    int nonSecretPayloadLength = 0)
        {
            var fatKeyBitSize = KeyBitSize * 2;

            // Error Checks
            if (fatKey == null || fatKey.Length != fatKeyBitSize / 8)
                throw new ArgumentException(String.Format("Key needs to be {0} bit!", fatKeyBitSize), "fatKey");

            byte[] cryptKey = fatKey.Take(fatKey.Length / 2).ToArray();
            byte[] authKey = fatKey.Skip(fatKey.Length / 2).ToArray();
            return Decrypt(secretMessage, cryptKey, authKey, nonSecretPayloadLength);
        }

        /// <summary>
        ///  Encryption (AES) then Authentication (HMAC) of a UTF8 message
        /// using Keys derived from a Password (PBKDF2).
        /// </summary>
        /// <param name="secretMessage">The secret message.</param>
        /// <param name="password">The password.</param>
        /// <param name="nonSecretPayload">The non secret payload.</param>
        /// <returns>
        /// Encrypted Message
        /// </returns>
        /// <exception cref="System.ArgumentException">password</exception>
        /// <remarks>
        /// Significantly less secure than using random binary keys.
        /// Adds additional non secret payload for key generation parameters.
        /// </remarks>
        public static string EncryptWithPassword(string secretMessage, string password,
                                 byte[] nonSecretPayload = null)
        {
            if (string.IsNullOrEmpty(secretMessage))
                throw new ArgumentException("Secret Message Required!", "secretMessage");

            var plainText = Encoding.UTF8.GetBytes(secretMessage);
            var cipherText = EncryptWithPassword(plainText, password, nonSecretPayload);
            return Convert.ToBase64String(cipherText);
        }

        /// <summary>
        ///  Authentication (HMAC) and then Descryption (AES) of a UTF8 Message
        /// using keys derived from a password (PBKDF2). 
        /// </summary>
        /// <param name="encryptedMessage">The encrypted message.</param>
        /// <param name="password">The password.</param>
        /// <param name="nonSecretPayloadLength">Length of the non secret payload.</param>
        /// <returns>
        /// Decrypted Message
        /// </returns>
        /// <exception cref="System.ArgumentException">Encrypted Message Required!;encryptedMessage</exception>
        /// <remarks>
        /// Significantly less secure than using random binary keys.
        /// </remarks>
        public static string DecryptWithPassword(string encryptedMessage, string password,
                                 int nonSecretPayloadLength = 0)
        {
            if (string.IsNullOrWhiteSpace(encryptedMessage))
                throw new ArgumentException("Encrypted Message Required!", "encryptedMessage");

            var cipherText = Convert.FromBase64String(encryptedMessage);
            var plainText = DecryptWithPassword(cipherText, password, nonSecretPayloadLength);
            return plainText == null ? null : Encoding.UTF8.GetString(plainText);
        }

        public static byte[] Encrypt(byte[] secretMessage, byte[] cryptKey, byte[] authKey, byte[] nonSecretPayload = null)
        {
            //User Error Checks
            if (cryptKey == null || cryptKey.Length != KeyBitSize / 8)
                throw new ArgumentException(String.Format("Key needs to be {0} bit!", KeyBitSize), "cryptKey");

            if (authKey == null || authKey.Length != KeyBitSize / 8)
                throw new ArgumentException(String.Format("Key needs to be {0} bit!", KeyBitSize), "authKey");

            if (secretMessage == null || secretMessage.Length < 1)
                throw new ArgumentException("Secret Message Required!", "secretMessage");

            //non-secret payload optional
            nonSecretPayload = nonSecretPayload ?? new byte[] { };

            byte[] cipherText;
            byte[] iv;

            using (var aes = new AesManaged
            {
                KeySize = KeyBitSize,
                BlockSize = BlockBitSize,
                Mode = CipherMode.CBC,
                Padding = PaddingMode.PKCS7
            })
            {

                //Use random IV
                aes.GenerateIV();
                iv = aes.IV;

                using (var encrypter = aes.CreateEncryptor(cryptKey, iv))
                using (var cipherStream = new MemoryStream())
                {
                    using (var cryptoStream = new CryptoStream(cipherStream, encrypter, CryptoStreamMode.Write))
                    using (var binaryWriter = new BinaryWriter(cryptoStream))
                    {
                        //Encrypt Data
                        binaryWriter.Write(secretMessage);
                    }

                    cipherText = cipherStream.ToArray();
                }

            }

            //Assemble encrypted message and add authentication
            using (var hmac = new HMACSHA256(authKey))
            using (var encryptedStream = new MemoryStream())
            {
                using (var binaryWriter = new BinaryWriter(encryptedStream))
                {
                    //Prepend non-secret payload if any
                    binaryWriter.Write(nonSecretPayload);
                    //Prepend IV
                    binaryWriter.Write(iv);
                    //Write Ciphertext
                    binaryWriter.Write(cipherText);
                    binaryWriter.Flush();

                    //Authenticate all data
                    var tag = hmac.ComputeHash(encryptedStream.ToArray());
                    //Postpend tag
                    binaryWriter.Write(tag);
                }
                return encryptedStream.ToArray();
            }

        }

        public static string EncryptFile(string inputFile, byte[] fatKey)
        {
            //User Error Checks
            if (fatKey == null || fatKey.Length != FatKeyBitSize / 8)
                throw new ArgumentException(String.Format("Key needs to be {0} bit!", FatKeyBitSize), "fatKey");

            byte[] cryptKey = fatKey.Take(fatKey.Length / 2).ToArray();
            byte[] authKey = fatKey.Skip(fatKey.Length / 2).ToArray();

            byte[] iv;

            using (var aes = new AesManaged
            {
                KeySize = KeyBitSize,
                BlockSize = BlockBitSize,
                Mode = CipherMode.CBC,
                Padding = PaddingMode.PKCS7
            })
            {
                //Use random IV
                aes.GenerateIV();
                iv = aes.IV;

                using var encrypter = aes.CreateEncryptor(cryptKey, iv);
                using var inputStream = new FileStream(inputFile, FileMode.Open);
                using (var cipherStream = new FileStream(inputFile + ".aes", FileMode.Create))
                {
                    using (var aesStream = new CryptoStream(cipherStream, encrypter, CryptoStreamMode.Write))
                    using (var binaryWriter = new BinaryWriter(aesStream))
                    {
                        byte[] buffer = new byte[1048576];
                        int read;

                        try
                        {
                            while ((read = inputStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                binaryWriter.Write(buffer, 0, read);
                            }
                            cipherStream.Flush();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error: " + ex.Message);
                            return null;
                        }
                    }
                }

                using (var cipherStream = new FileStream(inputFile + ".aes", FileMode.Open))
                {
                    //Assemble encrypted message and add authentication
                    using var hmac = new HMACSHA256(authKey);
                    using var encryptedStream = new FileStream(inputFile + ".aes.hmac", FileMode.Create);
                    using (var binaryWriter = new BinaryWriter(encryptedStream))
                    {
                        //Prepend IV
                        binaryWriter.Write(iv);
                        //Write Ciphertext
                        byte[] buffer = new byte[1048576];
                        int read;

                        try
                        {
                            // TODO OPEN CIPHERSTREAM
                            while ((read = cipherStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                binaryWriter.Write(buffer, 0, read);
                            }
                            binaryWriter.Flush();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error: " + ex.Message);
                            return null;
                        }

                        //Authenticate all data
                        var tag = hmac.ComputeHash(encryptedStream);
                        //Postpend tag
                        binaryWriter.Write(tag);
                    }
                }

                return inputFile + ".aes.hmac";
            }
        }

        public static string DecryptFile(string inputFile, byte[] fatKey)
        {
            //User Error Checks
            if (!inputFile.EndsWith(".aes.hmac"))
                throw new ArgumentException("Input File needs to have be a \".aes.hmac\"");

            if (fatKey == null || fatKey.Length != FatKeyBitSize / 8)
                throw new ArgumentException(String.Format("Key needs to be {0} bit!", FatKeyBitSize), "fatKey");

            byte[] cryptKey = fatKey.Take(fatKey.Length / 2).ToArray();
            byte[] authKey = fatKey.Skip(fatKey.Length / 2).ToArray();

            using var inputStream = new FileStream(inputFile, FileMode.Open);

            using (var hmac = new HMACSHA256(authKey))
            {
                var sentTag = new byte[hmac.HashSize / 8];
                //Grab Sent Tag
                inputStream.Position = inputStream.Length - sentTag.Length;
                inputStream.Read(sentTag, 0, sentTag.Length);

                //Calculate Tag
                inputStream.Position = 0;
                inputStream.SetLength(inputStream.Length - sentTag.Length);
                using var encryptedStream = inputStream;
                var calcTag = hmac.ComputeHash(encryptedStream);
                var ivLength = (BlockBitSize / 8);

                //if message length is to small just return null
                if (encryptedStream.Length < sentTag.Length + ivLength)
                    return null;

                //Compare Tag with constant time comparison
                var compare = 0;
                for (var i = 0; i < sentTag.Length; i++)
                    compare |= sentTag[i] ^ calcTag[i];

                //if message doesn't authenticate return null
                if (compare != 0)
                    return null;

                using (var aes = new AesManaged
                {
                    KeySize = KeyBitSize,
                    BlockSize = BlockBitSize,
                    Mode = CipherMode.CBC,
                    Padding = PaddingMode.PKCS7
                })
                {
                    int read;
                    byte[] buffer = new byte[1048576];

                    //Grab IV from message
                    var iv = new byte[ivLength];
                    read = encryptedStream.Read(iv, 0, ivLength);

                    using (var decrypter = aes.CreateDecryptor(cryptKey, iv))
                    using (var plainTextStream = new FileStream(inputFile.Remove(-".aes.hmac".Length), FileMode.Open))
                    {
                        using (var decrypterStream = new CryptoStream(plainTextStream, decrypter, CryptoStreamMode.Write))
                        using (var binaryWriter = new BinaryWriter(decrypterStream))
                        {
                            //Decrypt Cipher Text from Message
                            try
                            {
                                while ((read = encryptedStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    binaryWriter.Write(buffer, 0, read);
                                }
                                binaryWriter.Flush();
                            }
                            catch (CryptographicException ex_CryptographicException)
                            {
                                Console.WriteLine("CryptographicException error: " + ex_CryptographicException.Message);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Error: " + ex.Message);
                            }
                        }
                        //Return Plain Text
                        return inputFile.Remove(-".aes.hmac".Length);
                    }
                }
            }
        }

        public static byte[] Decrypt(byte[] encryptedMessage, byte[] cryptKey, byte[] authKey, int nonSecretPayloadLength = 0)
        {

            //Basic Usage Error Checks
            if (cryptKey == null || cryptKey.Length != KeyBitSize / 8)
                throw new ArgumentException(String.Format("CryptKey needs to be {0} bit!", KeyBitSize), "cryptKey");

            if (authKey == null || authKey.Length != KeyBitSize / 8)
                throw new ArgumentException(String.Format("AuthKey needs to be {0} bit!", KeyBitSize), "authKey");

            if (encryptedMessage == null || encryptedMessage.Length == 0)
                throw new ArgumentException("Encrypted Message Required!", "encryptedMessage");

            using (var hmac = new HMACSHA256(authKey))
            {
                var sentTag = new byte[hmac.HashSize / 8];
                //Calculate Tag
                var calcTag = hmac.ComputeHash(encryptedMessage, 0, encryptedMessage.Length - sentTag.Length);
                var ivLength = (BlockBitSize / 8);

                //if message length is to small just return null
                if (encryptedMessage.Length < sentTag.Length + nonSecretPayloadLength + ivLength)
                    return null;

                //Grab Sent Tag
                Array.Copy(encryptedMessage, encryptedMessage.Length - sentTag.Length, sentTag, 0, sentTag.Length);

                //Compare Tag with constant time comparison
                var compare = 0;
                for (var i = 0; i < sentTag.Length; i++)
                    compare |= sentTag[i] ^ calcTag[i];

                //if message doesn't authenticate return null
                if (compare != 0)
                    return null;

                using (var aes = new AesManaged
                {
                    KeySize = KeyBitSize,
                    BlockSize = BlockBitSize,
                    Mode = CipherMode.CBC,
                    Padding = PaddingMode.PKCS7
                })
                {

                    //Grab IV from message
                    var iv = new byte[ivLength];
                    Array.Copy(encryptedMessage, nonSecretPayloadLength, iv, 0, iv.Length);

                    using (var decrypter = aes.CreateDecryptor(cryptKey, iv))
                    using (var plainTextStream = new MemoryStream())
                    {
                        using (var decrypterStream = new CryptoStream(plainTextStream, decrypter, CryptoStreamMode.Write))
                        using (var binaryWriter = new BinaryWriter(decrypterStream))
                        {
                            //Decrypt Cipher Text from Message
                            binaryWriter.Write(
                              encryptedMessage,
                              nonSecretPayloadLength + iv.Length,
                              encryptedMessage.Length - nonSecretPayloadLength - iv.Length - sentTag.Length
                            );
                        }
                        //Return Plain Text
                        return plainTextStream.ToArray();
                    }
                }
            }
        }

        public static byte[] EncryptWithPassword(byte[] secretMessage, string password, byte[] nonSecretPayload = null)
        {
            nonSecretPayload = nonSecretPayload ?? new byte[] { };

            //User Error Checks
            if (secretMessage == null || secretMessage.Length == 0)
                throw new ArgumentException("Secret Message Required!", "secretMessage");

            var payload = new byte[((SaltBitSize / 8) * 2) + nonSecretPayload.Length];

            Array.Copy(nonSecretPayload, payload, nonSecretPayload.Length);
            int payloadIndex = nonSecretPayload.Length;

            byte[] cryptKey;
            byte[] authKey;
            //Use Random Salt to prevent pre-generated weak password attacks.
            using (var generator = new Rfc2898DeriveBytes(password, SaltBitSize / 8, Iterations))
            {
                var salt = generator.Salt;

                //Generate Keys
                cryptKey = generator.GetBytes(KeyBitSize / 8);

                //Create Non Secret Payload
                Array.Copy(salt, 0, payload, payloadIndex, salt.Length);
                payloadIndex += salt.Length;
            }

            //Deriving separate key, might be less efficient than using HKDF, 
            //but now compatible with RNEncryptor which had a very similar wireformat and requires less code than HKDF.
            using (var generator = new Rfc2898DeriveBytes(password, SaltBitSize / 8, Iterations))
            {
                var salt = generator.Salt;

                //Generate Keys
                authKey = generator.GetBytes(KeyBitSize / 8);

                //Create Rest of Non Secret Payload
                Array.Copy(salt, 0, payload, payloadIndex, salt.Length);
            }

            return Encrypt(secretMessage, cryptKey, authKey, payload);
        }

        public static byte[] DecryptWithPassword(byte[] encryptedMessage, string password, int nonSecretPayloadLength = 0)
        {
            //User Error Checks
            if (encryptedMessage == null || encryptedMessage.Length == 0)
                throw new ArgumentException("Encrypted Message Required!", "encryptedMessage");

            var cryptSalt = new byte[SaltBitSize / 8];
            var authSalt = new byte[SaltBitSize / 8];

            //Grab Salt from Non-Secret Payload
            Array.Copy(encryptedMessage, nonSecretPayloadLength, cryptSalt, 0, cryptSalt.Length);
            Array.Copy(encryptedMessage, nonSecretPayloadLength + cryptSalt.Length, authSalt, 0, authSalt.Length);

            byte[] cryptKey;
            byte[] authKey;

            //Generate crypt key
            using (var generator = new Rfc2898DeriveBytes(password, cryptSalt, Iterations))
            {
                cryptKey = generator.GetBytes(KeyBitSize / 8);
            }
            //Generate auth key
            using (var generator = new Rfc2898DeriveBytes(password, authSalt, Iterations))
            {
                authKey = generator.GetBytes(KeyBitSize / 8);
            }

            return Decrypt(encryptedMessage, cryptKey, authKey, cryptSalt.Length + authSalt.Length + nonSecretPayloadLength);
        }
    }
}
