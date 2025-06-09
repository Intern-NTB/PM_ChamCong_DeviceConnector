using Dapper;
using Shared.Interface;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SDK.Repository
{
    public class ChamCongRepository: IChamCongRepository
    {
        private readonly IDbConnection _connection;
        public ChamCongRepository(IDbConnection connection)
        {
            _connection = connection;
        }

        public async Task<int> SetChamCong(string maNhanVien, DateTime thoiGianChamCong)
        {
            if (_connection.State != ConnectionState.Open)
            {
                _connection.Open();
            }
            try

            {
                return await _connection.ExecuteAsync(
                    "sp_create_duLieuQuetVanTay",
                    new
                    {
                        maNhanVien = maNhanVien,
                        thoiGian = thoiGianChamCong
                    },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}

