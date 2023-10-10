
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using stage_api.Models;
using System.Configuration;

namespace stage_api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            builder.Services.AddScoped<AuthenticationService>();
            builder.Services.AddScoped<DataProcessingService>();
            builder.Services.AddScoped<FileUploadService>();

            builder.Services.AddDbContext<UserContext>(opt =>
           opt.UseInMemoryDatabase("Users"));
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowSpecificOrigin",
                    builder =>
                    {
                        builder.WithOrigins("http://localhost:4200") 
                               .AllowAnyMethod()
                               .AllowAnyHeader();
                    });
            });

           builder.Services.AddDbContext<dbContext>(options =>
               options.UseSqlite("Data Source=C:\\Users\\safaw\\Desktop\\Stage\\stage-db.db;"));
           


            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
          

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.UseCors("AllowSpecificOrigin");


            app.MapControllers();

            app.Run();
        }
    }
}