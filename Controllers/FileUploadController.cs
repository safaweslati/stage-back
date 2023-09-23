using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.SqlServer.Server;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using stage_api.Models;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO.Compression;

namespace stage_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileUploadController : ControllerBase
    {
        private readonly dbContext _dbContext;
        private readonly IWebHostEnvironment _hostingEnvironment;
        private string destinationTable ;


        public FileUploadController(dbContext dbContext, IWebHostEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;
            _dbContext = dbContext;
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


        [HttpPost("")]
        public IActionResult UploadExcelFile()
        {
            try
            {
                // Retrieve the uploaded Excel file from the request
                var formFile = Request.Form.Files[0];

                // Check if a file was uploaded
                if (formFile == null || formFile.Length == 0)
                {
                    return BadRequest("No file uploaded.");
                }

                // Read the file details from the request body
                var fileDetailsJson = Request.Form["fileInfos"];
                var fileDetails = JsonConvert.DeserializeObject<UploadedFile>(fileDetailsJson);

                // Extract the file name without the extension
                var fileName = Path.GetFileNameWithoutExtension(formFile.FileName);

                // Extract the file extension (including the dot)
                var fileExtension = Path.GetExtension(formFile.FileName);

                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");

                // Create the names for the Excel file and the ZIP file
                var excelFileName = $"{fileName + timestamp}{fileExtension}";
                var zipFileName = $"{fileName}.zip";

                // Create the full path to save the Excel file
                var excelFilePath = Path.Combine(Path.GetTempPath(), excelFileName);

                // Create a FileStream object to save the Excel file
                using (var stream = new FileStream(excelFilePath, FileMode.Create))
                {
                    // Copy the uploaded file to the FileStream
                    formFile.CopyTo(stream);
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

                this._dbContext.Files.Add(uploadedFile);
                this._dbContext.SaveChanges();

                return Ok(new { message = "File saved successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest("Error processing the file: " + ex.Message);
            }
        }





        [HttpGet("tables")]
        public async Task<IActionResult> GetTables()
        {

            using (var connection = _dbContext.Database.GetDbConnection())
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name <> 'UploadedFiles' AND name <> 'sqlite_sequence';";

                using (var reader = command.ExecuteReader())
                {
                    var tableNames = new List<string>();
                    while (reader.Read())
                    {
                        tableNames.Add(reader.GetString(0));
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
                    command.CommandText = $"PRAGMA table_info({table});";

                    using (var reader = command.ExecuteReader())
                    {
                        var columnNames = new List<string>();
                        while (reader.Read())
                        {
                            columnNames.Add(reader.GetString(reader.GetOrdinal("name")));
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

        [HttpPost("map-and-upload")]
        public IActionResult MapAndUpload()
        {
            try
            {
                var excelDataJson = Request.Form["excelData"];
                var mappingFormJson = Request.Form["mappingForm"];
                var destinationTable = Request.Form["destinationTable"];
                var excelData = JsonConvert.DeserializeObject<List<List<string>>>(excelDataJson.ToString());
                var mappingForm = JsonConvert.DeserializeObject<Dictionary<string, string>>(mappingFormJson.ToString());

                List<List<object>> transformedData = new List<List<object>>();

                List<string> dbFields = new List<string>();

                for (int j = 0; j < excelData[0].Count(); j++)
                {
                    dbFields.Add(null);
                }

                for (int i = 0; i < excelData.Count(); i++)
                {
                    List<object> newRow = new List<object>();
                    for (int j = 0; j < excelData[0].Count(); j++)
                    {
                        newRow.Add(null);
                    }
                    transformedData.Add(newRow);
                }

                foreach (var header in excelData[0])
                {
                    var dbField = mappingForm[header];

                    var action = mappingForm["action" + header];

                    int headerIndex = excelData[0].IndexOf(header);

                    dbFields[headerIndex] = dbField;
                    string dbFieldType = GetDbTypeFromSQLite(dbField, destinationTable);

                    var cropValue = mappingForm["cropValue" + header]; 


                    foreach (var row in excelData)
                    {

                        var value = row[headerIndex];
                        var rowIndex = excelData.IndexOf(row);
                        object newValue = null;

                        switch (action.ToLower())
                        {
                            case "convert":
                                newValue = ConvertAction(value, dbFieldType);
                                break;

                            case "crop": 
                                newValue = CropAction(value, cropValue);
                                break;

                                /*   case "filter":
                                       FilterAction(value);
                                       break;

                                   case "sort":
                                       SortAction(value);
                                       break;

                                   case "clean":
                                       CleanAction(value);
                                       break;*/

                        }



                        transformedData[rowIndex][headerIndex] = newValue;
                    }


                }



                var columns = string.Join(", ", dbFields);

                using (var connection = _dbContext.Database.GetDbConnection())
                {
                    connection.Open();

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            for (int i = 1; i < transformedData.Count; i++)
                            {
                                var transformedRow = transformedData[i];
                                var values = string.Join(", ", transformedRow.Select(value => $"'{value}'"));
                                var insertSql = $"INSERT INTO {destinationTable} ({columns}) VALUES ({values})";
                                Debug.WriteLine(insertSql);

                                using (var command = connection.CreateCommand())
                                {
                                    command.CommandText = insertSql;
                                    command.CommandType = CommandType.Text;
                                    command.ExecuteNonQuery();
                                }
                            }

                            transaction.Commit();
                            return Ok("Data inserted into the database successfully");
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            Debug.WriteLine($"SQLite Error: {ex.Message}");
                            return BadRequest(ex.Message);
                        }
                    }
                }



            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }


        private string GetDbTypeFromSQLite(string dbField, string table)
        {
            try
            {
                using (var connection = _dbContext.Database.GetDbConnection())
                {
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText = $"PRAGMA table_info({table});";

                    using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string columnName = reader["name"].ToString();
                                if (columnName.Equals(dbField, StringComparison.OrdinalIgnoreCase))
                                {
                                    return reader["type"].ToString();
                                }
                            }
                        }
                    }
                }
             
            catch(Exception ex) {
                Debug.WriteLine($"SQLite Error: {ex.Message}");
            }


            return "Unknown";
        }


        private object ConvertAction(object value, string dbFieldType)
        {
            if (value == DBNull.Value)
            {
                return DBNull.Value; 
            }

            switch (dbFieldType.ToLower())
            {
                case "integer":
                case "int":

                    int intValue;
                    if (int.TryParse(value.ToString(), out intValue))
                    {
                        return intValue;
                    }
                    break;

                case "real":
                case "float":

                    float floatValue;
                    if (float.TryParse(value.ToString(), out floatValue))
                    {
                        return floatValue;
                    }
                    break;

                case "text":

                        return value.ToString();


                case "date":
                    DateTime dateValue;
                    if (DateTime.TryParse(value.ToString(), out dateValue))
                    {
                        return dateValue.Date;
                    }
                    break;



                default:
                    return value;
                    break;
            }

            return null;
        }


        private object CropAction(object value, string cropValue)
        {
            if (value is string stringValue && int.TryParse(cropValue, out int maxLength) && stringValue.Length > maxLength)
            {
                return stringValue.Substring(0, maxLength);
            }

            return value; 
        }

    }
}