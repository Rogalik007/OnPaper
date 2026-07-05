using OnPaper.Common.Models.Utils;
using OnPaper.Models;

namespace OnPaper.Services.UsersService;

public interface IUsersService
{
    Task<ResultModel<User?>> GetUser(int id, CancellationToken cancellationToken);

    //Task<ResultModel<User>> UpdateUser(int id, CancellationToken cancellationToken);

    Task<ResultModel<User?>> DeleteUser(int id, CancellationToken cancellationToken);
}