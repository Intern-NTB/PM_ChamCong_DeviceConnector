using Shared.Entity;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Shared.Interface
{
    public interface INhanVienRepository
    {
        Task<IEnumerable<NhanVien>> GetAllNhanVienAsync();
        Task<int> SetNhanVienVanTay(NhanVienVanTay vanTay);
    }
}