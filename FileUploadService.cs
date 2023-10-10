using stage_api.Models;
using static stage_api.FileUploadService;
using System.IO.Compression;
using System.Diagnostics;

namespace stage_api
{
    
      public class FileUploadService 
      {
            private readonly dbContext _dbContext;
            private readonly IWebHostEnvironment _hostingEnvironment;

            public FileUploadService(dbContext dbContext, IWebHostEnvironment hostingEnvironment)
            {
                _dbContext = dbContext;
                _hostingEnvironment = hostingEnvironment;
            }

            public async Task<string> UploadExcelFile(IFormFile formFile, UploadedFile fileDetails)
            {
            try
            {
                // Extract the file name without the extension
                var fileName = Path.GetFileNameWithoutExtension(formFile.FileName);

                // Extract the file extension (including the dot)
                var fileExtension = Path.GetExtension(formFile.FileName);

                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");

                // Create the names for the Excel file and the ZIP file
                var excelFileName = $"{fileName + timestamp}{fileExtension}";
                var zipFileName = $"{fileName + timestamp}.zip";
                fileDetails.zipFileName = zipFileName;


                // Create the full path to save the Excel file
                var excelFilePath = Path.Combine(Path.GetTempPath(), excelFileName);

                // Create a FileStream object to save the Excel file
                using (var stream = new FileStream(excelFilePath, FileMode.Create))
                {
                    // Copy the uploaded file to the FileStream
                    await formFile.CopyToAsync(stream);
                }

                // Create the full path for the ZIP file
                var zipFilePath = Path.Combine(_hostingEnvironment.ContentRootPath, "zip files", zipFileName);

                // Create a ZipArchive object to create the ZIP file
                using (var zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
                {
                    // Add the Excel file to the ZIP archive with the same name
                    zipArchive.CreateEntryFromFile(excelFilePath, excelFileName);
                }

                // Store the zip file path and name in the database
                var uploadedFile = new UploadedFile
                {
                    fileName = fileDetails.fileName,
                    fileSize = fileDetails.fileSize,
                    destinationTable = fileDetails.destinationTable,
                    uploadDate = fileDetails.uploadDate,
                    uploadUser = fileDetails.uploadUser,
                    zipFileName = zipFileName
                };

                _dbContext.Files.Add(uploadedFile);
                _dbContext.SaveChanges();
                return "File saved successfully";
            }
            catch (Exception ex)
            {
                return "Error processing the file: " + ex.Message;
            }
            }

        public async Task<string> RollbackFileSave(IFormFile formFile, UploadedFile fileDetails)
        {
            try
            {
                if (formFile == null || fileDetails == null)
                {
                    return "Invalid arguments provided.";
                }

                Debug.WriteLine("Inside RollbackFileSave");

                var zipDirectoryPath = Path.Combine(_hostingEnvironment.ContentRootPath, "zip files");

           
                var zipFilePath = Path.Combine(zipDirectoryPath, fileDetails.zipFileName);

                Debug.WriteLine($"{zipFilePath}");

                if (File.Exists(zipFilePath))
                {
                    File.Delete(zipFilePath);
                }

                var uploadedFile = _dbContext.Files.FirstOrDefault(f => f.zipFileName == fileDetails.zipFileName);

                if (uploadedFile != null)
                {
                    _dbContext.Files.Remove(uploadedFile);
                    await _dbContext.SaveChangesAsync();  // Ensure changes are saved to the database
                }

                return "Rollback successful";
            }
            catch (Exception ex)
            {
                return "Error during rollback: " + ex.Message;
            }
        }

    }
}



