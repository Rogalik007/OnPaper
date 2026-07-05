using Microsoft.EntityFrameworkCore;
using OnPaper.Models;

namespace OnPaper.Infrastructure;

public class OnPaperDbContext(DbContextOptions<OnPaperDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; } = null!;
}
