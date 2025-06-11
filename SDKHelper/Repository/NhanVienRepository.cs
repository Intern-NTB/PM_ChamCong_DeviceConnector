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
                throw ex;
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
                "sp_upsert_vantay",
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
                throw ex;
            }
        }

        public async Task<IEnumerable<NhanVienVanTay>> GetAllNhanVienVanTay()
        {
            if (_connection.State != ConnectionState.Open)
            {
                _connection.Open();
            }

            try
            {
                var vanTays = await _connection.QueryAsync<NhanVienVanTay>(
                    "sp_read_all_vantay",
                    commandType: CommandType.StoredProcedure);

                return vanTays.ToList();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<int> BatchSetNhanVienVanTay(IEnumerable<NhanVienVanTay> vanTays)
        {
            if (_connection.State != ConnectionState.Open)
            {
                _connection.Open();
            }

            try
            {
                using (var transaction = _connection.BeginTransaction())
                {
                    try
                    {
                        int processedCount = 0;
                        foreach (var vanTay in vanTays)
                        {
                            var result = await _connection.ExecuteAsync(
                                "sp_upsert_vantay",
                                new
                                {
                                    MaNhanVien = vanTay.MaNhanVien,
                                    ViTriNgonTay = vanTay.ViTriNgonTay,
                                    DuLieuVanTay = vanTay.DuLieuVanTay
                                },
                                transaction,
                                commandType: CommandType.StoredProcedure);

                            // Dapper returns -1 for successful execution
                            if (result == -1)
                            {
                                processedCount++;
                            }
                        }

                        transaction.Commit();
                        return processedCount;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
