using System.Threading.Tasks;
using HybridDistributedCache.Models;

namespace HybridDistributedCache.Repositories
{
    public interface IMyComplexValueRepository
    {
        Task<MyComplexValue> GetAsync(int id);

        MyComplexValue Post(MyComplexValue value);

        MyComplexValue Put(int id, MyComplexValue value);

        void Remove(int id);
    }
}