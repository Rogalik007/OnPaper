namespace OnPaper.Common.Models.Utils;

// Ника как отдельного поля нет - используется часть email до "@"
public static class NicknameUtil
{
    public static string FromEmail(string email) => email.Split('@')[0];
}
