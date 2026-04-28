namespace TwitchTools.Web.Services.Security;

public interface IDataEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
