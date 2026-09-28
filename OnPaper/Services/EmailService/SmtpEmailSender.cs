using System;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OnPaper.Services.EmailService;

public class SmtpEmailSender(ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendEmailConfirmationAsync(string toEmail, string confirmationLink, CancellationToken cancellationToken)
    {
        var host = Environment.GetEnvironmentVariable("SMTP_HOST");

        const string subject = "Подтверждение почты OnPaper";
        var body =
            $"Здравствуйте!\n\n" +
            $"Чтобы подтвердить регистрацию в OnPaper, перейдите по ссылке:\n{confirmationLink}\n\n" +
            $"Ссылка действует 24 часа. Если вы не регистрировались в OnPaper, просто проигнорируйте это письмо.";

        // SMTP не настроен (локальная разработка) - вместо реальной отправки пишем
        // ссылку в лог, чтобы регистрацию можно было проверить и без почтового сервера.
        if (string.IsNullOrWhiteSpace(host))
        {
            logger.LogWarning(
                "SMTP is not configured. Email confirmation link for {Email}: {Link}", toEmail, confirmationLink);
            return;
        }

        var port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var parsedPort)
            ? parsedPort
            : 587;
        var user = Environment.GetEnvironmentVariable("SMTP_USER");
        var password = Environment.GetEnvironmentVariable("SMTP_PASSWORD");
        var from = Environment.GetEnvironmentVariable("SMTP_FROM") ?? user;

        using var client = new SmtpClient(host, port)
        {
            Credentials = new NetworkCredential(user, password),
            EnableSsl = true
        };

        using var message = new MailMessage(from!, toEmail, subject, body);

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            // Пользователь уже создан - не роняем регистрацию из-за сбоя почты,
            // но обязательно логируем ссылку, чтобы её можно было выдать вручную.
            logger.LogError(ex,
                "Failed to send confirmation email to {Email}. Link: {Link}", toEmail, confirmationLink);
        }
    }
}
