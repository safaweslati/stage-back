using Microsoft.EntityFrameworkCore;
using stage_api.Models;

public class dbContext : DbContext
{
    public dbContext(DbContextOptions<dbContext> options) : base(options)
    {
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UploadedFile>()
            .ToTable("UploadedFiles");
     
    }
    public virtual DbSet<UploadedFile> Files { get; set; }

}

