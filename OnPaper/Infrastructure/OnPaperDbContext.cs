using Microsoft.EntityFrameworkCore;
using OnPaper.Models;

namespace OnPaper.Infrastructure;

public class OnPaperDbContext(DbContextOptions<OnPaperDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Rating> Ratings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Один рейтинг на пользователя - очки за победы копятся в одной строке.
        modelBuilder.Entity<Rating>().HasIndex(r => r.UserId).IsUnique();
    }
}
