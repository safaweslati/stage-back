using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using stage_api.Models;
using System.Diagnostics;

namespace stage_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileUploadController : ControllerBase
    {
        private readonly dbContext _dbContext;
        private readonly FileUploadService _fileUploadService;
        private readonly DataProcessingService _dataProcessingService;

        private readonly IWebHostEnvironment _hostingEnvironment;


        public FileUploadController(dbContext dbContext, FileUploadService fileUploadService,DataProcessingService dataProcessingService, IWebHostEnvironment hostingEnvironment)
        {
            _fileUploadService = fileUploadService;
            _dbContext = dbContext;
            _dataProcessingService = dataProcessingService;
            _hostingEnvironment = hostingEnvironment;

        }



        [HttpGet("download/{fileName}")]
        public IActionResult DownloadFile(string fileName)
        {
            var filePath = Path.Combine(_hostingEnvironment.ContentRootPath, "zip files", fileName); // Adjust the path accordingly

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound();
            }

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            return File(fileBytes, "application/zip", fileName);
        }


 

        [HttpPost("map-and-upload")]
        public async Task<IActionResult> UploadAndProcessAsync()
        {
            

            try
            {
                // File saving logic
                var formFile = Request.Form.Files[0];
                // Check if a file was uploaded
                if (formFile == null || formFile.Length == 0)
                {
                    return BadRequest("No file uploaded.");
                }

                var fileDetailsJson = Request.Form["fileInfos"];
                var fileDetails = JsonConvert.DeserializeObject<UploadedFile>(fileDetailsJson);
                var fileSaveResult = await _fileUploadService.UploadExcelFile(formFile, fileDetails);
                if (fileSaveResult.Contains("Error"))
                {
                    
                    return BadRequest(fileSaveResult);
                }
            
            // Data uploading logic
            var excelDataJson = Request.Form["excelData"];
             var mappingFormJson = Request.Form["mappingForm"];
             var destinationTable = Request.Form["destinationTable"];
             var dataUploadResult = await _dataProcessingService.MapAndUploadData(excelDataJson, mappingFormJson, destinationTable);

             if (dataUploadResult.message.Contains("Error"))
             {
                    await _fileUploadService.RollbackFileSave(formFile, fileDetails);
                    return BadRequest(dataUploadResult);
             }

                return Ok(new { message = dataUploadResult.message, insertedData = dataUploadResult.transformedData });
            }
            catch (Exception ex)
            {
                return BadRequest("Error processing the request: " + ex.Message);
            }
       
        }



        [HttpGet("tables")]
        public async Task<IActionResult> GetTables()
        {
            using (var connection = _dbContext.Database.GetDbConnection())
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_name <> 'UploadedFiles';";

                using (var reader = command.ExecuteReader())
                {
                    var tableNames = new List<string>();
                    while (reader.Read())
                    {
                        string tableName = reader.GetString(0);

                        if (!tableName.Contains("AggregationResults"))
                        {
                            tableNames.Add(tableName);
                        }
                    }

                    return Ok(tableNames);
                }
            }
        }



        [HttpGet("files")]

        public async Task<IActionResult> GetFiles()
        {
            return Ok(this._dbContext.Files);
        }


        [HttpGet("destination-columns")]
        public async Task<IActionResult> GetDestinationTableColumns(string table)
        {
            try
            {
                if (string.IsNullOrEmpty(table))
                {
                    return BadRequest("Table name is required.");
                }

                using (var connection = _dbContext.Database.GetDbConnection())
                {
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = $"SELECT column_name FROM information_schema.columns WHERE table_name = '{table}';";

                    using (var reader = command.ExecuteReader())
                    {
                        var columnNames = new List<string>();
                        while (reader.Read())
                        {
                            columnNames.Add(reader.GetString(reader.GetOrdinal("column_name")));
                        }

                        return Ok(columnNames);
                    }
                }
            }
            catch (Exception ex)
            {
                return BadRequest("Error retrieving columns: " + ex.Message);
            }
        }




    }

}