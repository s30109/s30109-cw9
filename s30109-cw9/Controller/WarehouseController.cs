using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using s30109_cw9.DTO;

namespace s30109_cw9.Controller
{
    [Route("api/[controller]")]
    [ApiController]
    public class WarehouseController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public WarehouseController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost]
        public IActionResult AddProductToWarehouse(WarehouseRequest request)
        {
            if (request.Amount <= 0)
                return BadRequest("Amount must be greater than 0");

            using var connection = new SqlConnection(_configuration.GetConnectionString("Default"));
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                using var cmd1 = new SqlCommand("SELECT 1 FROM Product WHERE IdProduct = @id", connection, transaction);
                cmd1.Parameters.AddWithValue("@id", request.IdProduct);
                if (cmd1.ExecuteScalar() is null)
                    return NotFound("Product not found");

                using var cmd2 = new SqlCommand("SELECT 1 FROM Warehouse WHERE IdWarehouse = @id", connection,
                    transaction);
                cmd2.Parameters.AddWithValue("@id", request.IdWarehouse);
                if (cmd2.ExecuteScalar() is null)
                    return NotFound("Warehouse not found");

                int? idOrder = null;
                using (var cmd3 = new SqlCommand(
                           @"SELECT IdOrder FROM [Order]
                  WHERE IdProduct = @idProd AND Amount = @amt AND CreatedAt < @createdAt", connection, transaction))
                {
                    cmd3.Parameters.AddWithValue("@idProd", request.IdProduct);
                    cmd3.Parameters.AddWithValue("@amt", request.Amount);
                    cmd3.Parameters.AddWithValue("@createdAt", request.CreatedAt);

                    using var reader = cmd3.ExecuteReader();
                    if (reader.Read())
                        idOrder = reader.GetInt32(0);
                }

                if (idOrder == null)
                    return NotFound("Matching order not found");

                using var cmd4 = new SqlCommand(
                    "SELECT 1 FROM Product_Warehouse WHERE IdOrder = @id", connection, transaction);
                cmd4.Parameters.AddWithValue("@id", idOrder.Value);
                if (cmd4.ExecuteScalar() is not null)
                    return Conflict("Order already fulfilled");

                using var cmd5 = new SqlCommand(
                    "UPDATE [Order] SET FulfilledAt = GETDATE() WHERE IdOrder = @id", connection, transaction);
                cmd5.Parameters.AddWithValue("@id", idOrder.Value);
                cmd5.ExecuteNonQuery();

                decimal price;
                using (var cmd6 = new SqlCommand(
                           "SELECT Price FROM Product WHERE IdProduct = @id", connection, transaction))
                {
                    cmd6.Parameters.AddWithValue("@id", request.IdProduct);
                    price = (decimal)cmd6.ExecuteScalar();
                }

                using var cmd7 = new SqlCommand(
                    @"INSERT INTO Product_Warehouse (IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt)
                  OUTPUT INSERTED.IdProductWarehouse
                  VALUES (@wid, @pid, @oid, @amt, @price, GETDATE())", connection, transaction);
                cmd7.Parameters.AddWithValue("@wid", request.IdWarehouse);
                cmd7.Parameters.AddWithValue("@pid", request.IdProduct);
                cmd7.Parameters.AddWithValue("@oid", idOrder.Value);
                cmd7.Parameters.AddWithValue("@amt", request.Amount);
                cmd7.Parameters.AddWithValue("@price", price * request.Amount);

                int newId = (int)cmd7.ExecuteScalar();
                transaction.Commit();
                return Ok(newId);
            }
            catch
            {
                transaction.Rollback();
                return StatusCode(500, "Server error");
            }
        }

        [HttpPost("procedure")]
        public IActionResult AddProductWithProcedure(WarehouseRequest request)
        {
            using var connection = new SqlConnection(_configuration.GetConnectionString("Default"));
            using var command = new SqlCommand("AddProductToWarehouse", connection);
            command.CommandType = System.Data.CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@IdProduct", request.IdProduct);
            command.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
            command.Parameters.AddWithValue("@Amount", request.Amount);
            command.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);

            connection.Open();
            try
            {
                var result = command.ExecuteScalar();
                if (result is int id)
                    return Ok(id);
                return StatusCode(500, "Unexpected result");
            }
            catch (SqlException ex)
            {
                return BadRequest($"SQL Error: {ex.Message}");
            }
        }
    }
}
