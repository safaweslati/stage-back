using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using stage_api.Models;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace stage_api
{
    public class DataProcessingService 
    {
        private readonly dbContext _dbContext;

        public DataProcessingService(dbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<(string message, List<List<object>> transformedData)> MapAndUploadData(string excelDataJson, string mappingFormJson, string destinationTable)
        {
            try
            {
                var excelData = JsonConvert.DeserializeObject<List<List<string>>>(excelDataJson);
                var mappingForm = JsonConvert.DeserializeObject<Dictionary<string, string>>(mappingFormJson);

                var transformedData = InitializeTransformedData(excelData);
                var dbFields = new List<string>(new string[excelData[0].Count()]);
                var rowsToRemove = new List<int>();
                var sortInfoList = new List<SortInfo>();
                var aggregationResults = new Dictionary<string, object>();
                var filterConditions = new List<FilterCondition>();


                foreach (var header in excelData[0])
                {
                    var dbField = mappingForm[header];
                    var action = mappingForm["action" + header];
                    int headerIndex = excelData[0].IndexOf(header);

                    dbFields[headerIndex] = dbField;
                    string dbFieldType = GetDbTypeFromSqlServer(dbField, destinationTable);



                    foreach (var row in excelData)
                    {
                        var value = row[headerIndex];
                        var rowIndex = excelData.IndexOf(row);
                        object newValue = value;


                        switch (action.ToLower())
                        {
                            case "convert":
                                newValue = ConvertAction(value, dbFieldType);
                                break;


                            case "filter":

                                var filterOperator = mappingForm["filterOperator" + header];
                                var filterValue = mappingForm["filterValue" + header];
                                var filterCondition = new FilterCondition
                                {
                                    HeaderIndex = headerIndex,
                                    FilterOperator = filterOperator,
                                    FilterValue = filterValue
                                };

                                filterConditions.Add(filterCondition);
                                break;

                            case "sort":

                                sortInfoList.Add(new SortInfo
                                {
                                    HeaderIndex = headerIndex,
                                    SortOrder = mappingForm["sortOrder" + header],
                                    DataType = dbFieldType.ToLower()
                                });
                                break;
                        }

                          transformedData[rowIndex][headerIndex] = newValue;
                        }

                    
                }


                    RemoveRows(transformedData, filterConditions);


                     var columns = string.Join(", ", dbFields);
                    transformedData.RemoveAt(0);
                    foreach (var sortInfo in sortInfoList)
                    {
                        SortData(transformedData, sortInfo.HeaderIndex, sortInfo.SortOrder, sortInfo.DataType);
                    }


                    foreach (var header in excelData[0])
                    {
                    var dbField = mappingForm[header];
                    var action = mappingForm["action" + header];
                    string dbFieldType = GetDbTypeFromSqlServer(dbField, destinationTable);
                    int headerIndex = excelData[0].IndexOf(header);


                    if (action.ToLower() == "aggregate" && (IsNumericDbType(dbFieldType) || mappingForm["AggregateOperation" + header].ToLower() == "count"))
                    {
                        var aggregateOperation = mappingForm["AggregateOperation" + header];
                        Debug.WriteLine("before Agg action");
                        aggregationResults[dbField + '_' + aggregateOperation] = AggregateAction(transformedData, headerIndex, aggregateOperation);
                    }
                }

                InsertDataIntoDatabase(transformedData, destinationTable, columns);


                if (aggregationResults.Count > 0)
                {
                    CreateAggregationResultsTable(destinationTable, aggregationResults);
                    StoreAggregationResults(destinationTable, aggregationResults);
                }


                return ("Data processed and uploaded successfully", transformedData);

            }
            catch (Exception ex)
            {
                return ("Error processing the data: " + ex.Message, null);
            }
        }


        private bool IsNumericDbType(string dbFieldType)
        {
            var numericDbTypes = new List<string> { "INT", "SMALLINT", "TINYINT", "BIGINT", "FLOAT", "REAL", "DECIMAL", "NUMERIC", "MONEY", "SMALLMONEY", "DOUBLE", "BIT" };

            return numericDbTypes.Contains(dbFieldType.ToUpper());
        }


        private string GenerateUniqueAggregationTableName(string destinationTable)
        {
            string timestamp = DateTime.Now.ToString("MM/dd/yyyy-HH:mm:ss");


            string tableName = $"AggregationResults_{destinationTable}_{timestamp}";

            string sanitizedTableName = Regex.Replace(tableName, @"[^\w]+", "_");


            return sanitizedTableName;
        }



        private string _uniqueAggregationTableName;

        private void CreateAggregationResultsTable(string destinationTable, Dictionary<string, object> aggregationResults)
        {
            using (var connection = new SqlConnection(_dbContext.Database.GetDbConnection().ConnectionString))
            {
                try
                {
                    connection.Open();

                    using (var command = connection.CreateCommand())
                    {
                        if (string.IsNullOrEmpty(_uniqueAggregationTableName))
                        {
                            _uniqueAggregationTableName = GenerateUniqueAggregationTableName(destinationTable);
                        }

                        var tableName = _uniqueAggregationTableName;

                        // Create the AggregationResults table
                        var createTableSql = $"CREATE TABLE {tableName} (Id INT PRIMARY KEY IDENTITY, ";

                        foreach (var key in aggregationResults.Keys)
                        {
                            createTableSql += $"{key} DECIMAL, ";
                        }

                        createTableSql = createTableSql.TrimEnd(',', ' ') + ");";

                        try
                        {
                            Debug.WriteLine(createTableSql);
                            command.CommandText = createTableSql;
                            command.ExecuteNonQuery();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error creating AggregationResults table: {ex.Message}");
                            Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                            return;
                        }
                    }
                }
                finally
                {
                    // Ensure the connection is closed after use
                    if (connection.State == ConnectionState.Open)
                    {
                        connection.Close();
                    }
                }
            }
        }




        private void StoreAggregationResults(string destinationTable, Dictionary<string, object> aggregationResults)
        {
            using (var connection = new SqlConnection(_dbContext.Database.GetDbConnection().ConnectionString))
            {
                try
                {
                    connection.Open();

                    var tableName = _uniqueAggregationTableName;

                    var insertSql = $"INSERT INTO {tableName} (";

                    var parameters = new List<DbParameter>();

                    foreach (var key in aggregationResults.Keys)
                    {
                        insertSql += $"{key}, ";
                        parameters.Add(new SqlParameter($"@{key}", aggregationResults[key])); // Use SqlParameter consistently
                    }

                    insertSql = insertSql.TrimEnd(',', ' ') + ") VALUES (";

                    foreach (var key in aggregationResults.Keys)
                    {
                        insertSql += $"@{key}, ";
                    }

                    insertSql = insertSql.TrimEnd(',', ' ') + ");";

                    Debug.WriteLine("Generated SQL: " + insertSql);

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = insertSql;

                        // Add parameters
                        foreach (var parameter in parameters)
                        {
                            command.Parameters.Add(parameter);
                        }

                        command.ExecuteNonQuery();
                    }
                }
                finally
                {
                    // Ensure the connection is closed after use
                    if (connection.State == ConnectionState.Open)
                    {
                        connection.Close();
                    }
                }
            }
        }


        private string GetDbTypeFromSqlServer(string dbField, string table)
        {
            try
            {
                using (var connection = new SqlConnection(_dbContext.Database.GetDbConnection().ConnectionString))
                {
                    try
                    {

                        if (connection.State != ConnectionState.Open)
                        {
                            connection.Open();
                        }

                        var command = connection.CreateCommand();
                        command.CommandText = $"SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}';";

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string columnName = reader["COLUMN_NAME"].ToString();
                                if (columnName.Equals(dbField, StringComparison.OrdinalIgnoreCase))
                                {
                                    return reader["DATA_TYPE"].ToString();
                                }
                            }
                        }
                    }
                    finally
                    {
                        // Ensure the connection is closed after use
                        if (connection.State == ConnectionState.Open)
                        {
                            connection.Close();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SQL Server Error: {ex.Message}");
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
                case "int":
                case "smallint":
                case "tinyint":
                case "bigint":

                    int intValue;
                    if (int.TryParse(value.ToString(), out intValue))
                    {
                        return intValue;
                    }
                    else if (DateTime.TryParse(value.ToString(), out DateTime date))
                    {
                        return date.Year;
                    }
                    break;

                case "real":
                case "float":
                case "decimal":
                case "numeric":

                    decimal decimalValue;
                    if (decimal.TryParse(value.ToString(), out decimalValue))
                    {
                        return decimalValue;
                    }
                    break;

                case "bit":

                    bool boolValue;
                    if (bool.TryParse(value.ToString(), out boolValue))
                    {
                        return boolValue;
                    }
                    break;

                case "date":
                    DateTime dateOnlyValue;
                    if (DateTime.TryParse(value.ToString(), out dateOnlyValue))
                    {
                        return dateOnlyValue.Date.ToString("yyyy/MM/dd");

                    }
                    break;

                case "datetime":
                case "datetime2":
                case "smalldatetime":
                    DateTime dateTimeValue;
                    if (DateTime.TryParse(value.ToString(), out dateTimeValue))
                    {
                        return dateTimeValue;
                    }
                    break;


                case "uniqueidentifier":

                    Guid guidValue;
                    if (Guid.TryParse(value.ToString(), out guidValue))
                    {
                        return guidValue;
                    }
                    break;

                default:
                    return value;
                    break;
            }

            return null;
        }




        private bool FilterAction(object value, string filterOperator, string filterValue)
        {
            if (value == DBNull.Value)
            {
                return false;
            }


            switch (filterOperator)
            {
                case "GreaterThan":
                    if (float.TryParse(value.ToString(), out float numericValue) && float.TryParse(filterValue, out float numericFilterValue))
                    {
                        return numericValue > numericFilterValue;
                    }
                    return false;
                    break;


                case "LessThan":
                    if (float.TryParse(value.ToString(), out float numValue) && float.TryParse(filterValue, out float numFilterValue))
                    {
                        return numValue < numFilterValue;
                    }
                    return false;
                    break;


                case "Equals":
                    return value.ToString().Equals(filterValue, StringComparison.OrdinalIgnoreCase);

                case "NotEquals":
                    return !value.ToString().Equals(filterValue, StringComparison.OrdinalIgnoreCase);

                case "Contains":
                    return value.ToString().Contains(filterValue, StringComparison.OrdinalIgnoreCase);

                case "DoesNotContain":
                    return !value.ToString().Contains(filterValue, StringComparison.OrdinalIgnoreCase);

                default:
                    return false;
            }
            throw new ArgumentException($"Unrecognized filter operator: {filterOperator}");

 
        }


        private void RemoveRows(List<List<object>> transformedData, List<FilterCondition> filterConditions)
        {
            for (int rowIndex = transformedData.Count - 1; rowIndex > 0; rowIndex--)
            {
                var rowToRemove = false;
                foreach (var filterCondition in filterConditions)
                {
                    var rowValue = transformedData[rowIndex][filterCondition.HeaderIndex];
                    if (!FilterAction(rowValue, filterCondition.FilterOperator, filterCondition.FilterValue))
                    {
                        rowToRemove = true;
                        break;
                    }
                }

                if (rowToRemove)
                {
                    transformedData.RemoveAt(rowIndex);
                }

            }
        }


        private void SortData(List<List<object>> data, int headerIndex, string sortOrder, string dataType)
        {
            bool isAscending = string.Equals(sortOrder, "Asc", StringComparison.OrdinalIgnoreCase);

            data.Sort((row1, row2) =>
            {
                object value1 = row1[headerIndex];
                object value2 = row2[headerIndex];

                if (value1 == null && value2 == null)
                {
                    return 0; 
                }
                else if (value1 == null)
                {
                    return isAscending ? -1 : 1; 
                }
                else if (value2 == null)
                {
                    return isAscending ? 1 : -1; 
                }

                // Handle the comparison based on the specified data type
                switch (dataType.ToLower())
                {
                    case "date":
                        DateTime date1 = (DateTime)value1;
                        DateTime date2 = (DateTime)value2;
                        return isAscending ? DateTime.Compare(date1, date2) : DateTime.Compare(date2, date1);

                    case "int":
                    case "smallint":
                    case "bigint":
                        long long1 = Convert.ToInt64(value1);
                        long long2 = Convert.ToInt64(value2);
                        return isAscending ? long1.CompareTo(long2) : long2.CompareTo(long1);

                    case "tinyint":
                        byte byte1 = Convert.ToByte(value1);
                        byte byte2 = Convert.ToByte(value2);
                        return isAscending ? byte1.CompareTo(byte2) : byte2.CompareTo(byte1);

                    case "float":
                    case "real":
                    case "money":
                    case "smallmoney":
                        double double1 = Convert.ToDouble(value1);
                        double double2 = Convert.ToDouble(value2);
                        return isAscending ? double1.CompareTo(double2) : double2.CompareTo(double1);

                    case "decimal":
                        decimal decimal1 = Convert.ToDecimal(value1);
                        decimal decimal2 = Convert.ToDecimal(value2);
                        return isAscending ? decimal1.CompareTo(decimal2) : decimal2.CompareTo(decimal1);


                    default:
                        string str1 = value1.ToString();
                        string str2 = value2.ToString();
                        return isAscending ? string.Compare(str1, str2, StringComparison.OrdinalIgnoreCase) : string.Compare(str2, str1, StringComparison.OrdinalIgnoreCase);
                }

            });
        }



        private object AggregateAction(List<List<object>> data, int headerIndex, string aggregateOperation)
        {

            var values = data.Select(row => row[headerIndex]);

            switch (aggregateOperation.ToLower())
            {
                case "sum":
                    decimal? sum = values.Sum(value => Convert.ToDecimal(value));
                    return sum.HasValue ? (object)sum.Value : null;
                case "avg":
                    decimal? average = values.Average(value => Convert.ToDecimal(value));
                    return average.HasValue ? (object)average.Value : null;
                case "count":
                    int count = values.Count(value => value != null);
                    return (object)count;
                case "min":
                    decimal? min = values.Min(value => Convert.ToDecimal(value));
                    return min.HasValue ? (object)min.Value : null;
                case "max":
                    decimal? max = values.Max(value => Convert.ToDecimal(value));
                    return max.HasValue ? (object)max.Value : null;
                default:
                    return null;
            }
        }



        private List<List<object>> InitializeTransformedData(List<List<string>> excelData)
        {
            var transformedData = new List<List<object>>();

            for (int i = 0; i < excelData.Count(); i++)
            {
                var newRow = new List<object>();
                for (int j = 0; j < excelData[0].Count(); j++)
                {
                    newRow.Add(null);
                }
                transformedData.Add(newRow);
            }

            return transformedData;
        }



        private void InsertDataIntoDatabase(List<List<object>> transformedData, string destinationTable, string columns)
        {
            Debug.WriteLine("inside the insert");

            // Use a new SqlConnection instance with the correct connection string
            using (var connection = new SqlConnection(_dbContext.Database.GetDbConnection().ConnectionString))
            {
                try
                {
                    if (connection == null)
                    {
                        Debug.WriteLine("Connection is null.");
                    }

                    Debug.WriteLine(connection.ConnectionString);

                    connection.Open();
                    Debug.WriteLine("conn opened");

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            Debug.WriteLine("transaction began");

                            for (int i = 0; i < transformedData.Count; i++)
                            {
                                var values = string.Join(", ", transformedData[i].Select(value => $"'{value}'"));
                                var insertSql = $"INSERT INTO {destinationTable} ({columns}) VALUES ({values})";
                                Debug.WriteLine(insertSql);

                                using (var command = connection.CreateCommand())
                                {
                                    command.Transaction = transaction; 
                                    command.CommandText = insertSql;
                                    command.CommandType = CommandType.Text;
                                    command.ExecuteNonQuery();
                                }
                            }

                            transaction.Commit();
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            Debug.WriteLine($"Error: {ex.Message}");
                            throw;
                        }
                    }
                }
                finally
                {
                    // Ensure the connection is closed after use
                    if (connection.State == ConnectionState.Open)
                    {
                        connection.Close();
                    }
                }
            }
        }

    }
}


