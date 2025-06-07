using Dapper;
using Shared.Entity;
using Shared.Interface;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace SDK.Repository
{
    public class NhanVienRepository : INhanVienRepository
    {
        private readonly IDbConnection _connection; 
        public NhanVienRepository(IDbConnection connection)
        {
            _connection = connection;
        }

        public async Task<IEnumerable<NhanVien>> GetAllNhanVienAsync()
        {
            // Check if connection is open
            if (_connection.State != ConnectionState.Open)
            {
                _connection.Open();
            }
            try
            {
                var nhanViens = await _connection.QueryAsync<NhanVien>(
                    "sp_read_nhanvien",
                    commandType: CommandType.StoredProcedure);
                        
                return nhanViens.ToList();
            }
            catch (Exception ex)
            {
                // Consider logging the exception
                throw;
            }
        }

        public async Task<int> SetNhanVienVanTay(NhanVienVanTay vanTay)
        {
            if (_connection.State != ConnectionState.Open)
            {
                _connection.Open();
            }

            try
            {
                return await _connection.ExecuteAsync(
                "sp_create_vantay",
                new
                {
                    MaNhanVien = vanTay.MaNhanVien,
                    ViTriNgonTay = vanTay.ViTriNgonTay,
                    DuLieuVanTay = vanTay.DuLieuVanTay
                },
                commandType: System.Data.CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }
}
