using System.Threading;
using System.Threading.Tasks;
using OnPaper.Common.Models.Utils;

namespace OnPaper.Services.AuthService;

public interface IAuthService
{
    Task<ResultModel<AuthModel>> Login(string email, string password, CancellationToken cancellationToken);
    Task<ResultModel<AuthModel>> Register(string email, string password, CancellationToken cancellationToken);
}
