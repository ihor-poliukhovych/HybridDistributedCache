using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using HybridDistributedCache.Models;
using HybridDistributedCache.Repositories;

namespace HybridDistributedCache.Controllers
{
    [Route("api/[controller]")]
    public class ValuesController : Controller
    {
        private readonly IMyComplexValueRepository _myComplexValueRepository;

        public ValuesController(IMyComplexValueRepository myComplexValueRepository)
        {
            this._myComplexValueRepository = myComplexValueRepository;
        }
        
        [HttpGet("{id}")]
        public async Task<MyComplexValue> Get(int id)
        {
            return await this._myComplexValueRepository.GetAsync(id);
        }
        
        [HttpPost]
        public MyComplexValue Post([FromBody]MyComplexValue value)
        {
            return this._myComplexValueRepository.Post(value);
        }
        
        [HttpPut("{id}")]
        public MyComplexValue Put(int id, [FromBody]MyComplexValue value)
        {
            return this._myComplexValueRepository.Put(id, value);
        }
        
        [HttpDelete("{id}")]
        public NoContentResult DeleteAsync(int id)
        {
            this._myComplexValueRepository.Remove(id);
            return new NoContentResult();
        }
    }
}
