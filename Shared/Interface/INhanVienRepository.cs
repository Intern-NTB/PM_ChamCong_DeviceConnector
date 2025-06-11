using Shared.Entity;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Shared.Interface
{
    public interface INhanVienRepository
    {
        Task<IEnumerable<NhanVien>> GetAllNhanVienAsync();
        Task<IEnumerable<NhanVienVanTay>> GetAllNhanVienVanTay();
        Task<IEnumerable<NhanVienVanTay>> GetNhanVienVanTay(int employeeId);
        Task<int> SetNhanVienVanTay(NhanVienVanTay vanTay);
        Task<int> BatchSetNhanVienVanTay(IEnumerable<NhanVienVanTay> vanTays);
        Task<int> DeleteNhanVienVanTay(int maNhanVien, int viTriNgonTay);
    }
}