namespace Sms.Application.Security;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
