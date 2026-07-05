using Microsoft.EntityFrameworkCore;
using OnPaper.Common.Enums;
using OnPaper.Common.Models.Utils;
using OnPaper.Infrastructure;
using OnPaper.Models;

namespace OnPaper.Services.UsersService;

public class UsersService(OnPaperDbContext dbContext) : IUsersService
{
    public async Task<ResultModel<User?>> GetUser(int id, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user == null)
        {
            return ResultModel.Fail<User?>("User with id = " + id + " does not exist", ErrorType.BadRequest);
        }
        
        return ResultModel.Ok<User?>(user);
    }

    public async Task<ResultModel<User?>> DeleteUser(int id, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user == null)
        {
            return ResultModel.Fail<User?>("User with id = " + id + " does not exist", ErrorType.BadRequest);
        }

        dbContext.Users.Remove(user);

        var isSuccess = false;
        try
        {
            isSuccess = await dbContext.SaveChangesAsync(cancellationToken) > 0;
        }
        catch
        {
            isSuccess = false;
        }

        if (!isSuccess)
        {
            return ResultModel.Fail<User?>("Internal server error", ErrorType.ServerError);
        }

        return ResultModel.Ok<User?>(user);
    }
}
