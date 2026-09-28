using System.Threading;
using System.Threading.Tasks;

namespace OnPaper.Services.EmailService;

public interface IEmailSender
{
    Task SendEmailConfirmationAsync(string toEmail, string confirmationLink, CancellationToken cancellationToken);
}
