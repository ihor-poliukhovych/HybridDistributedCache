using System;

namespace HybridDistributedCache.Models
{
    public class MyValue
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        public MyValue()
        {

        }

        public MyValue(Guid id, string name)
        {
            this.Id = id;
            this.Name = name;
        }
    }
}